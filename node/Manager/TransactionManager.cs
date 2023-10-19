using System.Diagnostics;
using System.Numerics;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
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
    private IVerifier Verifier { get; }
    private IStateCache StateCache { get; }
    private ILogger Logger { get; }

    public TransactionManager(IStoreRepository repository, IKeyRepository keyRepository, IVerifier verifier, IStateCache stateCache, IExecutorFactory executorFactory, ILogger logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        KeyRepository = keyRepository ?? throw new ArgumentNullException(nameof(keyRepository));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
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

                Repository.SetStake(validator, stake, 0);
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
            LogError(ex, $"{CHAIN_NAME}AddGenesis error");
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
                if (StateCache.TryGet(entry.Value.To, out var ledger))
                {
                    ledger.Pending = checked (ledger.Pending - entry.Value.Value);
                }
            }

            StateCache.GetBlocks().Clear();

            Logger.LogDebug($"Pending votes: {StateCache.GetVotes().Count}");

            var chainState = StateCache.GetCurrentState();

            foreach (var votehash in view.Votes)
            {
                if (!StateCache.GetVotes().TryGetValue(votehash, out var vote))
                {
                    throw new Exception($"unknown reference to vote ({votehash})");
                }

                var stakeAddress = vote.PublicKey.ToAddress();
                var stake = Repository.GetStake(stakeAddress);

                if (stake is null)
                {
                    throw new Exception($"not validator ({stakeAddress})");
                }

                var stakeAmount = stake.Stake;
                var isSeedValidator = Constant.SEED_VALIDATORS.Contains(stakeAddress);

                if (isSeedValidator)
                {
                    stakeAmount = Constant.MIN_STAKE;
                    seedStake = checked(seedStake + stakeAmount);
                }

                totalStake = checked(totalStake + stakeAmount);

                if (!isSeedValidator)
                {
                    var voteReward = new Transaction
                    {
                        TransactionType = TransactionType.STAKE_REWARD,
                        PublicKey = vote.PublicKey,
                        To = stake.RewardAddress,
                        Value = stakeAmount, // Executor will update this to final value!!
                        Data = votehash,
                        Timestamp = view.Timestamp
                    };

                    toExecute.Add(voteReward);
                }

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

            if (view.Id % Constant.VOTE_INTERVAL == 1)
            {
                StateCache.GetVotes().Clear();

                // TODO: maybe we include this in View hash / signature to prevent tampering?
                var devFee = new Transaction
                {
                    TransactionType = TransactionType.DEV_REWARD,
                    To = Constant.DEV_FEE_ADDRESS,
                    Value = Constant.DEV_REWARD,
                    Timestamp = view.Timestamp
                };

                toExecute.Add(devFee);
            }

            var context = new ExecutorContext(Repository, StateCache.GetLedgers(), StateCache.GetCurrentView(), totalStake - seedStake, height);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute, view);

            chainState.ViewHash = view.GetHash();
            chainState.Id++;
            chainState.Weight += (chainState.CurrentDifficulty.ToWork() * (totalStake / Constant.MIN_STAKE)) + chainState.CurrentDifficulty.ToWork();
            chainState.Votes += votes.Count;
            chainState.Transactions += toExecute.Count;
            chainState.Blocks += blocks.Count;
            chainState.CurrentDifficulty = CalculateDifficulty(chainState.CurrentDifficulty.ToWork(), blocks.Count);

            Repository.AddRange(blocks);
            Repository.AddRange(votes);
            Repository.AddRange(toExecute);
            Repository.Add(view);
            Repository.SaveState(chainState);

            var isMilestone = view.Id % Constant.VOTE_INTERVAL == 0;
            var node = KeyRepository.GetKey();
            var address = node!.PublicKey.ToAddress();
            var shouldVote = castVote && isMilestone && Repository.IsValidator(address);

            Logger.LogDebug($"Should vote: castVote = {castVote}, isMilestone = {isMilestone}, isvalidator = {Repository.IsValidator(address)}");

            if (shouldVote)
            {
                var stake = Repository.GetStake(address);
                var vote = new Vote
                {
                    ViewHash = view.GetHash(),
                    PublicKey = node.PublicKey
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
            LogInformation($"{CHAIN_NAME}Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [Transactions = {toExecute.Count}] [Blocks = {blocks.Count}] [Votes = {votes.Count}] [Next difficulty = {chainState.CurrentDifficulty}]");

            return true;
        }
        catch (Exception ex)
        {
            LogError(ex, $"{CHAIN_NAME}AddView error");

            StateCache.Clear();

            StateCache.SetView(Repository.GetLastView()!);
            StateCache.SetChainState(Repository.GetChainState()!);

            dbtx.Rollback();
        }

        return false;
    }

    protected bool AddBlockInternal(Block block, bool broadcast)
    {
        var sw = Stopwatch.StartNew();
        
        if (!StateCache.TryGet(block.To, out var to))
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

        LogInformation($"{CHAIN_NAME}Added block #{chainState.Blocks + StateCache.GetBlocks().Count} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

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
        if (tx.TransactionType == TransactionType.REG_VALIDATOR)
        {
            return AddValidatorRegInternal(tx, broadcast);
        }

        try
        {
            if (!StateCache.TryGet(tx.From!, out var from))
            {
                from = Repository.GetWallet(tx.From!) ?? new Ledger(tx.From!);
                StateCache.Add(from);
            }

            if (from.Balance < tx.Value)
            {
                tx.ExecutionResult = ExecutionResult.TOO_LOW_BALANCE;
                LogInformation($"{CHAIN_NAME}AddTransaction rejected (reason = too low balance, balance = {from.Balance}, value = {tx.Value})");
                return false;
            }

            if (!StateCache.TryGet(tx.To!, out var to))
            {
                to = Repository.GetWallet(tx.To!) ?? new Ledger(tx.To!);
                StateCache.Add(to);
            }

            checked
            {
                from.Balance -= tx.Value;
                to.Pending += tx.Value;
            }

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
            LogError(ex, $"{CHAIN_NAME}AddTransaction error");
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
            LogError(ex, $"{CHAIN_NAME}AddVote error.");
        }

        return false;
    }

    protected bool AddValidatorRegInternal(Transaction tx, bool broadcast)
    {
        try
        {
            if (!StateCache.TryGet(tx.From!, out var from))
            {
                from = Repository.GetWallet(tx.From!) ?? new Ledger(tx.From!);
                StateCache.Add(from);
            }

            var stake = Repository.GetStake(tx.From!);
            var balance = from.Balance;

            checked
            {
                balance = from.Balance + (stake?.Stake ?? 0);
            }

            if (balance < tx.Value)
            {
                LogInformation($"{CHAIN_NAME}AddValidatorReg rejected (reason = too low balance)");
                return false;
            }

            StateCache.Add(tx);

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex) 
        {
            LogError(ex, $"{CHAIN_NAME}AddValidatorReg error");
        }

        return false;
    }

    protected bool loggingDisabled;

    private void LogInformation(string msg)
    {
        if (loggingDisabled)
        {
            return;
        }

        Logger.LogInformation(msg);
    }

    private void LogInformation(Exception ex, string msg)
    {
        if (loggingDisabled)
        {
            return;
        }

        Logger.LogInformation(ex, msg);
    }

    private void LogError(string msg)
    {
        Logger.LogError(msg);
    }

    private void LogError(Exception ex, string msg)
    {
        Logger.LogError(ex, msg);
    }

    private Difficulty CalculateDifficulty(BigInteger currentWork, int blockCount)
    {
        if (blockCount == 0)
        {
            var nextTarget = currentWork / 4 * 3;
            var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

            return BigInteger.Max(minTarget, nextTarget).ToDifficulty();
        }
        else
        {
            var totalWork = currentWork * blockCount;
            return totalWork.ToDifficulty();
        }
    }
}
