using System.Diagnostics;
using System.Numerics;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Procedure;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public abstract class TransactionManager
{
    private IExecutorFactory ExecutorFactory { get; }
    private IStoreRepository Repository { get; }
    private IKeyRepository KeyRepository { get; }
    private IStateCache StateCache { get; }
    private ILogger Logger { get; }

    public TransactionManager(IStoreRepository repository, IKeyRepository keyRepository, IStateCache stateCache, IExecutorFactory executorFactory, ILogger logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        KeyRepository = keyRepository ?? throw new ArgumentNullException(nameof(keyRepository));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract void Broadcast(View view);
    public abstract void Broadcast(Transaction tx);
    public abstract void Broadcast(Block block);
    public abstract void Broadcast(Vote vote);

    public abstract void Publish(EventBase ev);
    public abstract void Publish(List<EventBase> events);
    public abstract string CHAIN_NAME { get; }

    public bool AddGenesis(View view)
    {
        using var dbtx = Repository.BeginTransaction();

        try
        {
            var chainState = new ChainState
            {
                Id = 0,
                ViewHash = view.GetHash(),
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            foreach (var validator in Constant.SEED_VALIDATORS)
            {
                var stake = new Validator
                {
                    NodeAddress = validator,
                    Stake = 0,
                    RewardAddress = Address.NULL_ADDRESS
                };

                Repository.SetStake(validator, stake);
            }

            StateCache.SetChainState(chainState);
            StateCache.SetView(view);

            Repository.SaveState(chainState);
            Repository.Add(view);

            dbtx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{CHAIN_NAME}AddGenesis error", CHAIN_NAME);
            dbtx.Rollback();
        }

        return false;
    }

    protected bool AddViewInternal(View view, bool broadcast, bool castVote)
    {
        var sw = Stopwatch.StartNew();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            // make sure there are no leftover rewards from other nodes
            view.Rewards.Clear();

            var height = view.Id;

            var toExecute = new List<Transaction>(StateCache.TransactionCount() + StateCache.GetBlocks().Count + StateCache.GetVotes().Count);
            var blocks = new List<Block>(StateCache.GetBlocks().Count);
            var votes = new List<Vote>(StateCache.GetVotes().Count);
            var totalStake = 0UL;
            var seedStake = 0UL;

            foreach (var blockhash in view.Blocks)
            {
                if (!StateCache.GetBlocks().Remove(blockhash, out var block))
                {
                    throw new Exception($"unknown reference to block ({blockhash})");
                }

                var blockReward = new Transaction
                {
                    TransactionType = TransactionType.BLOCK_REWARD,
                    To = block.To,
                    Value = block.Value,
                    Data = blockhash,
                    Timestamp = view.Timestamp
                };

                toExecute.Add(blockReward);

                blocks.Add(block);
            }

            // Reset pending values for orphaned blocks
            foreach (var entry in StateCache.GetBlocks())
            {
                if (StateCache.GetLedgers().TryGetValue(entry.Value.To, out var ledger))
                {
                    ledger.Pending = checked (ledger.Pending - entry.Value.Value);
                }
            }

            StateCache.GetBlocks().Clear();

            Logger.LogDebug("Pending votes: {voteCount}", StateCache.GetVotes().Count);

            var chainState = StateCache.GetCurrentState();

            foreach (var votehash in view.Votes)
            {
                if (!StateCache.GetVotes().TryGetValue(votehash, out var vote))
                {
                    throw new Exception($"unknown reference to vote ({votehash})");
                }

                var stakeAddress = vote.PublicKey.ToAddress();
                var stake = Repository.GetStake(stakeAddress) ?? throw new Exception($"not validator ({stakeAddress})");
                var stakeAmount = stake.Stake;
                var isSeedValidator = Constant.SEED_VALIDATORS.Contains(stakeAddress);

                if (isSeedValidator)
                {
                    stakeAmount = Constant.MIN_STAKE;
                    seedStake = checked(seedStake + stakeAmount);
                }

                totalStake = checked(totalStake + stakeAmount);

                votes.Add(vote);
            }

            foreach (var txId in view.Transactions)
            {
                if (!StateCache.GetTransactions().Remove(txId, out var tx))
                {
                    throw new Exception($"unknown reference to transaction ({txId})");
                }

                toExecute.Add(tx);
            }

            if (view.ShouldClearVotes())
            {
                StateCache.GetVotes().Clear();
            }

            if (view.IsEpoch())
            {
                HandleEpochChange(view, toExecute);
            }

            var context = new ExecutorContext(Repository, StateCache.GetLedgers(), StateCache.GetValidators(), StateCache.GetCurrentView(), totalStake - seedStake, height);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute, view);

            chainState.ViewHash = view.GetHash();
            chainState.Id++;
            chainState.Weight += (chainState.CurrentDifficulty.ToWork() * (totalStake / Constant.MIN_STAKE)) + chainState.CurrentDifficulty.ToWork();
            chainState.Votes += votes.Count;
            chainState.Transactions += toExecute.Count;
            chainState.Blocks += blocks.Count;

            var lastView = Repository.GetView(chainState.Id - 1);
            var lastState = Repository.GetChainState(chainState.Id - 1);
            chainState.CurrentDifficulty = chainState.CurrentDifficulty.ScaleDifficulty(blocks.Count, lastState?.CalculateWork(lastView?.Blocks.Count ?? 0) ?? 0);

            Repository.AddRange(blocks);
            Repository.AddRange(votes);
            Repository.AddRange(toExecute);
            Repository.Add(view);
            Repository.SaveState(chainState);

            var node = KeyRepository.GetKey();
            var address = node!.PublicKey.ToAddress();
            var shouldVote = castVote && view.IsMilestone() && Repository.IsValidator(address);

            if (shouldVote)
            {
                var validator = Repository.GetStake(address) ?? throw new Exception("failed to load stake for current node, corrupted Validator index?");

                var stake = validator.Stake;

                if (Constant.SEED_VALIDATORS.Contains(address))
                {
                    stake = Constant.MIN_STAKE;
                }

                var vote = new Vote
                {
                    ViewHash = view.GetHash(),
                    PublicKey = node.PublicKey,
                    Stake = validator.Stake,
                    RewardAddress = validator.RewardAddress
                };

                vote.Sign(node.PrivateKey);
                AddVoteInternal(vote, true);
            }

            dbtx.Commit();

            if (broadcast)
            {
                Broadcast(view);
            }

            Publish(chainState);
            Publish(StateCache.GetLedgers().Values.Select(x => (EventBase)x).ToList());
            Publish(StateCache.GetValidators().Values.Select(x => (EventBase)x).ToList());
            Publish(context.GetEvents());

            foreach (var ledger in StateCache.GetLedgers().Values)
            {
                if (ledger.Pending == 0)
                {
                    StateCache.GetLedgers().Remove(ledger.Address);
                }
            }

            StateCache.SetView(view);

            sw.Stop();
            Logger.LogInformation("{CHAIN_NAME}Added view #{height} in {duration}ms [Transactions = {txCount}] [Blocks = {blockCount}] [Votes = {voteCount}] [Next difficulty = {nextDifficulty}]",
                CHAIN_NAME,
                height,
                sw.Elapsed.TotalMilliseconds,
                toExecute.Count,
                blocks.Count,
                votes.Count,
                chainState.CurrentDifficulty
            );

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{CHAIN_NAME}AddView error", CHAIN_NAME);

            StateCache.Clear();

            StateCache.SetView(Repository.GetLastView()!);
            StateCache.SetChainState(Repository.GetChainState()!);

            dbtx.Rollback();
        }

        return false;
    }

    private void HandleEpochChange(View view, List<Transaction> toExecute)
    {
        var milestones = Constant.EPOCH_LENGTH / Constant.VOTE_INTERVAL;
        var epochEnd = view.Id;
        var epochStart = Math.Max(epochEnd - Constant.EPOCH_LENGTH, 0);
        var totalStake = 0UL;
        var totalReward = RewardCalculator.ValidatorReward(view.Id) * (ulong)milestones;
        var blockCount = 0;

        var aggregatedVotes = new Dictionary<Address, (PublicKey PublicKey, Address RewardAddress, ulong CumulatedStake)>();

        for (var i = epochStart; i < epochEnd; i += Constant.VOTE_INTERVAL)
        {
            // +1 due to votes being registered on next view after epoch
            var voteView = Repository.GetView(i + 1);

            if (voteView is null)
            {
                break;
            }

            blockCount += voteView.Blocks.Count;

            var votes = Repository.GetVotes(voteView.Votes);

            foreach (var vote in votes)
            {
                var address = vote.PublicKey.ToAddress();

                if (Constant.SEED_VALIDATORS.Contains(address))
                {
                    // no credit for seed validators
                    continue;
                }

                totalStake += vote.Stake;

                if (!aggregatedVotes.ContainsKey(address))
                {
                    aggregatedVotes.Add(address, (vote.PublicKey, vote.RewardAddress, vote.Stake));
                    continue;
                }

                var validator = aggregatedVotes[address];
                validator.CumulatedStake = checked(validator.CumulatedStake + vote.Stake);
                validator.RewardAddress = vote.RewardAddress;
            }
        }

        foreach (var agg in aggregatedVotes)
        {
            var voteReward = new Transaction
            {
                TransactionType = TransactionType.STAKE_REWARD,
                PublicKey = agg.Value.PublicKey,
                To = agg.Value.RewardAddress,
                Value = (ulong)Math.Floor(checked(totalReward * (agg.Value.CumulatedStake / (double)totalStake))),
                Timestamp = view.Timestamp
            };

            toExecute.Add(voteReward);
        }

        var devRewardBlock = RewardCalculator.DevRewardForBlock(view.Id) * (ulong)blockCount;
        var devRewardVal = RewardCalculator.DevRewardForValidator(view.Id) * (ulong)milestones;

        var devFee = new Transaction
        {
            TransactionType = TransactionType.DEV_REWARD,
            To = Constant.DEV_FEE_ADDRESS,
            Value = devRewardBlock + devRewardVal,
            Timestamp = view.Timestamp
        };

        toExecute.Add(devFee);
    }

    protected bool AddBlockInternal(Block block, bool broadcast)
    {
        var sw = Stopwatch.StartNew();
        
        if (!StateCache.GetLedgers().TryGetWallet(block.To, Repository, out var to))
        {
            to = Repository.GetWallet(block.To) ?? new Ledger(block.To);
            StateCache.Add(to);
        }

        checked
        {
            to.Pending += block.Value;
        }

        var chainState = StateCache.GetCurrentState();

        sw.Stop();

        Logger.LogInformation("{CHAIN_NAME}Added block #{blockNumber} in {duration}ms [diff = {difficulty}]",
            CHAIN_NAME,
            chainState.Blocks + StateCache.GetBlocks().Count,
            sw.Elapsed.TotalMilliseconds,
            block.Difficulty
        );

        if (broadcast)
        {
            Broadcast(block);
        }

        StateCache.Add(block);

        Publish(to);

        return true;
    }

    protected bool AddTransactionInternal(Transaction tx, bool broadcast)
    {
        if (tx.TransactionType == TransactionType.REGISTER_VALIDATOR)
        {
            return AddValidatorRegisterationInternal(tx, broadcast);
        }
        else if (tx.TransactionType == TransactionType.DEREGISTER_VALIDATOR)
        {
            return AddValidatorDeregisterationInternal(tx, broadcast);
        }

        try
        {
            var transfer = new Transfer(Repository, StateCache.GetLedgers(), StateCache.GetValidators());

            if (!transfer.From(tx.From!, tx.Value, out var executionResult, out var from))
            {
                tx.ExecutionResult = executionResult;
                return false;
            }

            transfer.Pending(tx.To, tx.Value, out var to);

            StateCache.Add(tx);

            Publish(from);
            Publish(to);

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex) 
        {
            Logger.LogError(ex, "{CHAIN_NAME}AddTransaction error", CHAIN_NAME);
        }

        return false;
    }

    protected bool AddValidatorRegisterationInternal(Transaction tx, bool broadcast)
    {
        try
        {
            if (!StateCache.GetLedgers().TryGetWallet(tx.From!, Repository, out var from))
            {
                tx.ExecutionResult = ExecutionResult.UNKNOWN;
                return false;
            }

            if (from.Balance < Constant.MIN_STAKE)
            {
                tx.ExecutionResult = ExecutionResult.TOO_LOW_BALANCE;
                return false;
            }

            // Move balance to pending indicating it will be locked
            from.Pending = from.Balance;
            from.Balance = 0;

            StateCache.Add(tx);

            Publish(from);

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex) 
        {
            Logger.LogError(ex, "{CHAIN_NAME}AddValidatorRegisteration error", CHAIN_NAME);
        }

        return false;
    }

    protected bool AddValidatorDeregisterationInternal(Transaction tx, bool broadcast)
    {
        try
        {
            if (!StateCache.GetLedgers().TryGetWallet(tx.From!, Repository, out var from))
            {
                tx.ExecutionResult = ExecutionResult.UNKNOWN;
                return false;
            }

            if (!StateCache.GetValidators().TryGetValidator(tx.From!, Repository, out var validator))
            {
                tx.ExecutionResult = ExecutionResult.UNKNOWN;
                return false;
            }

            // Move stake to pending indicating it will be unlocked
            from.Pending = validator.Stake;

            StateCache.Add(tx);

            Publish(from);

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex) 
        {
            Logger.LogError(ex, "{CHAIN_NAME}AddValidatorDeregisteration error", CHAIN_NAME);
        }

        return false;
    }

    protected bool AddVoteInternal(Vote vote, bool broadcast)
    {
        try
        {
            StateCache.Add(vote);

            if (broadcast)
            {
                Broadcast(vote);
            }

            Logger.LogDebug("Added vote");

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{CHAIN_NAME}AddVote error.", CHAIN_NAME);
        }

        return false;
    }

    protected bool loggingDisabled;
}
