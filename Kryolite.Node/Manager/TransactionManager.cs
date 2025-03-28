using System.Diagnostics;
using System.Numerics;
using Kryolite.Interface;
using Kryolite.Node.Executor;
using Kryolite.Node.Procedure;
using Kryolite.Node.Storage.Key;
using Kryolite.Model;
using Kryolite.Type;
using Kryolite.Shared;
using Microsoft.Extensions.Logging;
using Kryolite.Module.SmartContract;

namespace Kryolite.Node;

public abstract class TransactionManager
{
    private readonly IStoreRepository _repository;
    private readonly IKeyRepository _keyRepository;
    private readonly IVirtualMachineFactory _vmFactory;
    private readonly IStateCache _stateCache;
    private readonly ILogger _logger;

    public TransactionManager(IStoreRepository repository, IKeyRepository keyRepository, IVirtualMachineFactory vmFactory, IStateCache stateCache, ILogger logger)
    {
        _repository = repository;
        _keyRepository = keyRepository;
        _vmFactory = vmFactory;
        _stateCache = stateCache;
        _logger = logger;
    }

    public abstract void Broadcast(View view);
    public abstract void Broadcast(Transaction tx);
    public abstract void Broadcast(Block block);
    public abstract void Broadcast(Vote vote);
    public abstract void Publish(IEvent ev);
    public abstract void Publish(List<IEvent> ev);
    public abstract string CHAIN_NAME { get; }

    public bool AddGenesis(View view)
    {
        using var dbtx = _repository.BeginTransaction();

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

                _repository.SetValidator(0, stake);
            }

            _stateCache.SetChainState(chainState);
            _stateCache.SetView(view);

            _repository.SaveState(chainState);
            _repository.Add(view);

            dbtx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{CHAIN_NAME}AddGenesis error", CHAIN_NAME);
            dbtx.Rollback();
        }

        return false;
    }

    protected bool AddViewInternal(View view, bool broadcast, bool castVote)
    {
        var sw = Stopwatch.StartNew();
        using var dbtx = _repository.BeginTransaction();

        try
        {
            // make sure there are no leftover rewards from other nodes
            view.Rewards.Clear();

            var height = view.Id;

            var toExecute = new List<Transaction>(view.Transactions.Count + view.Blocks.Count + view.Votes.Count);
            var blocks = new List<Block>(view.Blocks.Count);
            var votes = new List<Vote>(view.Votes.Count);
            var totalStake = 0UL;
            var seedStake = 0UL;

            var chainState = _stateCache.GetCurrentState();
            var blockRewards = new Dictionary<Address, ulong>();

            foreach (var blockhash in view.Blocks)
            {
                if (!_stateCache.GetBlocks().Remove(blockhash, out var block))
                {
                    throw new Exception($"unknown reference to block ({blockhash})");
                }

                if (!blockRewards.TryGetValue(block.To, out var count))
                {
                    blockRewards.Add(block.To, 0);
                }

                blockRewards[block.To] = count + 1;
                blocks.Add(block);
            }

            foreach (var (to, reward) in blockRewards)
            {
                var blocksSubmitted = reward;
                var blockReward = new Transaction
                {
                    TransactionType = TransactionType.BLOCK_REWARD,
                    To = to,
                    Value = chainState.BlockReward / (ulong)view.Blocks.Count * blocksSubmitted,
                    Timestamp = view.Timestamp
                };

                toExecute.Add(blockReward);
            }

            // Reset pending values for orphaned blocks
            foreach (var entry in _stateCache.GetBlocks())
            {
                if (_stateCache.GetLedgers().TryGetValue(entry.Value.To, out var ledger))
                {
                    ledger.Pending = checked(ledger.Pending - entry.Value.Value);
                }
            }

            _stateCache.GetBlocks().Clear();

            _logger.LogDebug("Pending votes: {voteCount}", _stateCache.GetVotes().Count);

            foreach (var votehash in view.Votes)
            {
                if (!_stateCache.GetVotes().TryGetValue(votehash, out var vote))
                {
                    throw new Exception($"unknown reference to vote ({votehash})");
                }

                var stakeAddress = vote.PublicKey.ToAddress();

                if (!_stateCache.GetValidators().TryGetValidator(stakeAddress, _repository, out var validator))
                {
                    throw new Exception($"not validator ({stakeAddress})");
                }

                var stakeAmount = validator.Stake;
                var isSeedValidator = Constant.SEED_VALIDATORS.Contains(stakeAddress);

                if (isSeedValidator)
                {
                    stakeAmount = Constant.MIN_STAKE;
                    seedStake = checked(seedStake + stakeAmount);
                }

                if (!validator.Active)
                {
                    // Reactivate the validator
                    chainState.TotalActiveStake = checked(chainState.TotalActiveStake + stakeAmount);
                    validator.Active = true;
                }

                validator.LastActiveHeight = height;
                validator.Changed = true;

                totalStake = checked(totalStake + stakeAmount);

                votes.Add(vote);
            }

            // Remove transactions that were applied in this view
            foreach (var txId in view.Transactions)
            {
                if (!_stateCache.GetTransactions().Remove(txId, out var tx))
                {
                    throw new Exception($"unknown reference to transaction ({txId})");
                }

                toExecute.Add(tx);
            }

            if (view.ShouldClearVotes())
            {
                _stateCache.GetVotes().Clear();
            }

            if (view.IsEpoch())
            {
                HandleEpochChange(view, chainState, toExecute);
            }

            var context = new ExecutorContext(_repository, _stateCache.GetLedgers(), _stateCache.GetValidators(), _stateCache.GetCurrentView(), totalStake - seedStake, height);
            var executor = new Executor.Executor(context, _vmFactory, _logger);

            executor.Execute(toExecute, view, chainState);

            if (blocks.Count > 0)
            {
                // there were blocks found, reset accumulated reward
                chainState.BlockReward = 0;
            }

            var work = chainState.CurrentDifficulty.ToWork();
            var finalized = totalStake >= (chainState.TotalActiveStake * 0.66d);

            // Update chain state
            chainState.ViewHash = view.GetHash();
            chainState.Id++;
            chainState.Weight += (work * (totalStake / Constant.MIN_STAKE)) + work;
            chainState.TotalWork += work * blocks.Count;
            chainState.TotalVotes += votes.Count;
            chainState.TotalTransactions += toExecute.Count;
            chainState.TotalBlocks += blocks.Count;
            chainState.CurrentDifficulty = DifficultyScale.Scale(chainState, _repository);
            chainState.BlockReward += RewardCalculator.BlockReward(view.Id);

            if (finalized)
            {
                FinalizeView(chainState);
            }

            _repository.AddRange(blocks);
            _repository.AddRange(votes);
            _repository.AddRange(toExecute);
            _repository.Add(view);
            _repository.SaveState(chainState);

            // Commit before voting for GetStake to get updated stake value
            dbtx.Commit();

            var pubKey = _keyRepository.GetPublicKey();
            var address = pubKey.ToAddress();
            var shouldVote = castVote && view.IsMilestone() && _repository.IsValidator(address);

            if (shouldVote)
            {
                var validator = _repository.GetValidator(address) ?? throw new Exception("failed to load stake for current node, corrupted Validator index?");

                var stake = validator.Stake;

                if (Constant.SEED_VALIDATORS.Contains(address))
                {
                    stake = Constant.MIN_STAKE;
                }

                var vote = new Vote
                {
                    ViewHash = view.GetHash(),
                    PublicKey = pubKey,
                    Stake = stake,
                    RewardAddress = validator.RewardAddress
                };

                vote.Sign(_keyRepository.GetPrivateKey());
                AddVoteInternal(vote, true);
            }

            if (broadcast)
            {
                Broadcast(view);
            }

            Publish(chainState);
            Publish(_stateCache.GetLedgers().Values.Select(x => (IEvent)x).ToList());
            Publish(_stateCache.GetValidators().Values.Select(x => (IEvent)x).ToList());
            Publish(context.GetEvents());

            foreach (var ledger in _stateCache.GetLedgers().Values)
            {
                // Remove cached ledgers that do not have anything pending
                if (ledger.Pending == 0)
                {
                    _stateCache.GetLedgers().Remove(ledger.Address);
                }
            }

            _stateCache.SetView(view);

            sw.Stop();
            _logger.LogInformation("{CHAIN_NAME}Added view #{height} in {duration}ms [Transactions = {txCount}] [Blocks = {blockCount}] [Votes = {voteCount}] [Next difficulty = {nextDifficulty}]",
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
            _logger.LogError(ex, "{CHAIN_NAME}AddView error", CHAIN_NAME);

            _stateCache.Clear();

            _stateCache.SetView(_repository.GetLastView()!);
            _stateCache.SetChainState(_repository.GetChainState()!);

            dbtx.Rollback();
        }

        return false;
    }

    protected void HandleEpochChange(View view, ChainState chainState, List<Transaction> toExecute)
    {
        var milestones = Constant.EPOCH_LENGTH / Constant.VOTE_INTERVAL;
        var epochEnd = view.Id;
        var epochStart = Math.Max(epochEnd - Constant.EPOCH_LENGTH, 0);
        var totalStake = 0UL;
        var totalReward = RewardCalculator.ValidatorReward(view.Id) * (ulong)milestones;
        var blockCount = 0;

        var aggregatedVotes = new Dictionary<Address, AggregatedData>();
        var voters = new HashSet<Address>();

        for (var i = epochStart; i < epochEnd; i += Constant.VOTE_INTERVAL)
        {
            // +1 due to votes being registered on next view after epoch
            var voteView = _repository.GetView(i + 1);

            if (voteView is null)
            {
                break;
            }

            blockCount += voteView.Blocks.Count;

            var votes = _repository.GetVotes(voteView.Votes);

            foreach (var vote in votes)
            {
                var address = vote.PublicKey.ToAddress();
                voters.Add(address);

                if (Constant.SEED_VALIDATORS.Contains(address))
                {
                    // no credit for seed validators
                    continue;
                }

                totalStake += vote.Stake;

                if (!aggregatedVotes.TryGetValue(address, out var validator))
                {
                    aggregatedVotes.Add(address, new(vote.PublicKey, vote.RewardAddress, vote.Stake));
                    continue;
                }

                validator.CumulatedStake = checked(validator.CumulatedStake + vote.Stake);
                validator.RewardAddress = vote.RewardAddress;
            }
        }

        foreach (var agg in aggregatedVotes)
        {
            var stakeReward = (ulong)Math.Floor(checked(totalReward * (agg.Value.CumulatedStake / (double)totalStake)));
            var collectedFees = (ulong)Math.Floor(checked(chainState.CollectedFees * (agg.Value.CumulatedStake / (double)totalStake)));

            var voteReward = new Transaction
            {
                TransactionType = TransactionType.STAKE_REWARD,
                PublicKey = agg.Value.PublicKey,
                To = agg.Value.RewardAddress,
                Value = stakeReward + collectedFees,
                Timestamp = view.Timestamp
            };

            toExecute.Add(voteReward);
        }

        chainState.TotalActiveStake = 0;

        // Recalculate TotalActiveState based on voters during previous epoch
        foreach (var validatorEntry in _repository.GetValidators())
        {
            // We might have cached value if the stake has been updated during this view
            if (!_stateCache.GetValidators().TryGetValidator(validatorEntry.NodeAddress, _repository, out var validator))
            {
                throw new Exception("failed to load validator through cache");
            }

            if (voters.Contains(validator.NodeAddress))
            {
                var stake = Constant.SEED_VALIDATORS.Contains(validator.NodeAddress) ? Constant.MIN_STAKE : validator.Stake;
                chainState.TotalActiveStake = checked(chainState.TotalActiveStake + stake);
            }
            else
            {
                // If the validator was active but did not cast a single vote, then mark it inactive
                if (validator.Active)
                {
                    validator.Active = false;
                    validator.Changed = true;
                }
            }
        }

        var devRewardBlock = RewardCalculator.DevRewardForBlock(view.Id) * (ulong)blockCount;
        var devRewardVal = RewardCalculator.DevRewardForValidator(view.Id) * (ulong)milestones;

        var devFee = new Transaction
        {
            TransactionType = TransactionType.DEV_REWARD,
            To = Constant.DEV_FEE_ADDRESS,
            Value = checked(devRewardBlock + devRewardVal),
            Timestamp = view.Timestamp
        };

        toExecute.Add(devFee);

        chainState.CollectedFees = 0;
    }

    private void FinalizeView(ChainState chainState)
    {
        var height = chainState.Id - 1;
        chainState.LastFinalizedHeight = height;

        _repository.DeleteNonLatestFromIndexBeforeHeight(LedgerKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(ContractKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(ContractCodeKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(ContractSnapshotKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(TokenKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(ValidatorKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(TokenIdKey.KeyName, height);
        _repository.DeleteNonLatestFromIndexBeforeHeight(TokenLedgerKey.KeyName, height);
    }

    protected bool AddBlockInternal(Block block, bool broadcast)
    {
        var sw = Stopwatch.StartNew();

        var chainState = _stateCache.GetCurrentState();

        if (broadcast)
        {
            Broadcast(block);
        }

        _stateCache.Add(block);

        sw.Stop();

        _logger.LogInformation("{CHAIN_NAME}Added block #{blockNumber} in {duration}ms [diff = {difficulty}]",
            CHAIN_NAME,
            chainState.TotalBlocks + _stateCache.GetBlocks().Count,
            sw.Elapsed.TotalMilliseconds,
            block.Difficulty
        );

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
            var transfer = new Transfer(_repository, _stateCache.GetLedgers(), _stateCache.GetValidators(), _stateCache.GetCurrentState());
            var totalValue = tx.Value + (ulong)tx.MaxFee;

            if (!transfer.From(tx.From!, totalValue, out var executionResult, out var from))
            {
                tx.ExecutionResult = executionResult;
                return false;
            }

            _stateCache.Add(tx);

            Publish(from);

            if (tx.TransactionType != TransactionType.CONTRACT)
            {
                transfer.Pending(tx.To, tx.Value, out var to);
                Publish(to);
            }

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{CHAIN_NAME}AddTransaction error", CHAIN_NAME);
        }

        return false;
    }

    protected bool AddValidatorRegisterationInternal(Transaction tx, bool broadcast)
    {
        try
        {
            if (!_stateCache.GetLedgers().TryGetWallet(tx.From!, _repository, out var from))
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

            _stateCache.Add(tx);

            Publish(from);

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{CHAIN_NAME}AddValidatorRegisteration error", CHAIN_NAME);
        }

        return false;
    }

    protected bool AddValidatorDeregisterationInternal(Transaction tx, bool broadcast)
    {
        try
        {
            if (!_stateCache.GetLedgers().TryGetWallet(tx.From!, _repository, out var from))
            {
                tx.ExecutionResult = ExecutionResult.UNKNOWN;
                return false;
            }

            if (!_stateCache.GetValidators().TryGetValidator(tx.From!, _repository, out var validator))
            {
                tx.ExecutionResult = ExecutionResult.UNKNOWN;
                return false;
            }

            // Move stake to pending indicating it will be unlocked
            from.Pending = validator.Stake;

            _stateCache.Add(tx);

            Publish(from);

            if (broadcast)
            {
                Broadcast(tx);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{CHAIN_NAME}AddValidatorDeregisteration error", CHAIN_NAME);
        }

        return false;
    }

    protected bool AddVoteInternal(Vote vote, bool broadcast)
    {
        try
        {
            _stateCache.Add(vote);

            if (broadcast)
            {
                Broadcast(vote);
            }

            _logger.LogDebug("Added vote");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{CHAIN_NAME}AddVote error.", CHAIN_NAME);
        }

        return false;
    }

    protected bool loggingDisabled;

    private class AggregatedData(PublicKey publicKey, Address rewardAddress, ulong cumulatedStake)
    {
        public PublicKey PublicKey { get; set; } = publicKey;
        public Address RewardAddress { get; set; } = rewardAddress;
        public ulong CumulatedStake { get; set; } = cumulatedStake;
    }
}
