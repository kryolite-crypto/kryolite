using Kryolite.ByteSerializer;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Network;
using Kryolite.Node.Repository;
using Kryolite.RocksDb;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class StoreManager : TransactionManager, IStoreManager
{
    private IStoreRepository Repository { get; }
    private IEventBus EventBus { get; }
    private IStateCache StateCache { get; }
    private IVerifier Verifier { get; }
    private ILogger<StoreManager> Logger { get; }

    public override string CHAIN_NAME => "";

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    public StoreManager(IStoreRepository repository, IKeyRepository keyRepository, IEventBus eventBus, IStateCache stateCache, IVerifier verifier, ILogger<StoreManager> logger) : base(repository, keyRepository, stateCache, logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool BlockExists(SHA256Hash blockhash)
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetBlocks().ContainsKey(blockhash) || Repository.BlockExists(blockhash);
    }

    public bool VoteExists(SHA256Hash votehash)
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetVotes().ContainsKey(votehash) || Repository.VoteExists(votehash);
    }

    public bool TransactionExists(SHA256Hash hash)
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetTransactions().ContainsKey(hash) || Repository.TransactionExists(hash);
    }

    public bool AddView(View view, bool broadcast, bool castVote)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (!Verifier.Verify(view))
        {
            return false;
        }

        return AddViewInternal(view, broadcast, castVote);
    }

    public bool AddBlock(Block block, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        try
        {
            if (!Verifier.Verify(block))
            {
                return false;
            }

            return AddBlockInternal(block, broadcast);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public bool AddBlock(BlockTemplate blocktemplate, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        try
        {
            var block = new Block
            {
                To = blocktemplate.To,
                Timestamp = blocktemplate.Timestamp,
                LastHash = blocktemplate.ParentHash,
                Difficulty = blocktemplate.Difficulty,
                Nonce = blocktemplate.Solution,
                Value = blocktemplate.Value
            };

            if (!Verifier.Verify(block))
            {
                return false;
            }

            return AddBlockInternal(block, broadcast);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public ExecutionResult AddTransaction(TransactionDto txDto, bool broadcast)
    {
        try
        {
            using var _ = rwlock.EnterWriteLockEx();

            var tx = new Transaction(txDto);

            if (!Verifier.Verify(tx))
            {
                return ExecutionResult.VERIFY_FAILED;
            }

            AddTransactionInternal(tx, broadcast);

            return tx.ExecutionResult;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddTransaction error");
            return ExecutionResult.UNKNOWN;
        }
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (!Verifier.Verify(vote))
        {
            return false;
        }

        return AddVoteInternal(vote, broadcast);
    }

    public List<Block> GetBlocks(List<SHA256Hash> blockhashes)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetBlocks(blockhashes);
    }

    public List<Vote> GetVotes(List<SHA256Hash> votehashes)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetVotes(votehashes);
    }

    public List<Transaction> GetTransactions(List<SHA256Hash> transactionIds)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetTransactions(transactionIds);
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetVotesAtHeight(height);
    }

    public View? GetLastView()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetLastView();
    }

    public BlockTemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = StateCache.GetCurrentState();
        var block = new Block
        {
            To = wallet,
            Value = chainState.BlockReward,
            Timestamp = timestamp,
            LastHash = chainState.ViewHash,
            Difficulty = chainState.CurrentDifficulty
        };

        return new BlockTemplate
        {
            Height = chainState.Id,
            To = wallet,
            Value = block.Value,
            Difficulty = chainState.CurrentDifficulty,
            ParentHash = block.LastHash,
            Nonce = block.GetBaseHash(),
            Timestamp = block.Timestamp
        };
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetChainState()!;
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = rwlock.EnterReadLockEx();

        var chainState = Repository.GetChainState();
        return chainState!.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetCurrentState()?.Id ?? 0;
    }

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetLastNTransctions(address, count);
    }

    public ulong GetBalance(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetWallet(address)?.Balance ?? 0;
    }

    public Validator? GetStake(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetStake(address);
    }

    public void ResetChain()
    {
        using var _ = rwlock.EnterWriteLockEx();
        Repository.Reset();
    }

    public Contract? GetContract(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetContract(address);
    }

    public List<Ledger> GetRichList(int count)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetRichList(count);
    }
    public List<Transaction> GetTransactionsForAddress(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetTransactions(address);
    }

    public Transaction? GetTransactionForHash(SHA256Hash hash)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (StateCache.GetTransactions().TryGetValue(hash, out var tx))
        {
            return tx;
        }

        return Repository.GetTransaction(hash);
    }

    public Ledger? GetLedger(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (StateCache.GetLedgers().TryGetWallet(address, Repository, out var ledger))
        {
            return ledger;
        }

        return null;
    }

    public string? CallContractMethod(Address address, CallMethod call, out ulong gasFee)
    {
        var payload = new TransactionPayload
        {
            Payload = call
        };

        var tx = new Transaction
        {
            To = address,
            Data = Serializer.Serialize(payload)
        };

        return CallContractMethod(tx, false, out gasFee);
    }

    public string? CallContractMethod(Transaction tx, bool simulateTransfer, out ulong gasFee)
    {
        using var _ = rwlock.EnterReadLockEx();

        var contract = Repository.GetContract(tx.To) ?? throw new Exception(ExecutionResult.INVALID_CONTRACT.ToString());
        var snapshot = Repository.GetLatestSnapshot(tx.To) ?? throw new Exception(ExecutionResult.CONTRACT_SNAPSHOT_MISSING.ToString());
        var balance = GetBalance(tx.To);

        if (simulateTransfer)
        {
            balance += tx.Value;
        }

        var payload = Serializer.Deserialize<TransactionPayload>(tx.Data);

        if (payload?.Payload is not CallMethod call)
        {
            gasFee = 0;
            return null;
        }

        var methodName = $"{call.Method}";
        var methodParams = new List<object>();
        var method = (contract.Manifest?.Methods
            .Where(x => x.Name == methodName)
            .FirstOrDefault()) ?? throw new Exception(ExecutionResult.INVALID_METHOD.ToString());

        if (call.Params is not null)
        {
            methodParams.AddRange(call.Params);
        }

        var vmContext = new VMContext(Repository.GetLastView()!, contract, tx, Random.Shared, Logger, balance);

        var vm = KryoVM.LoadFromSnapshot(tx.To, Repository, snapshot)
            .WithContext(vmContext);

        vm.Fuel = uint.MaxValue;

        var ret = vm.CallMethod(methodName, [.. methodParams], out var json);

        gasFee = uint.MaxValue - vm.Fuel;

        return json;
    }

    public Token? GetToken(Address contract, SHA256Hash tokenId)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetToken(contract, tokenId);
    }

    public List<Token> GetTokens(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetTokens(address);
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetContractTokens(contractAddress);
    }

    public View? GetView(long height)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetView(height);
    }

    public View? GetView(SHA256Hash viewHash)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetView(viewHash);
    }

    public Block? GetBlock(SHA256Hash blockhash)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (StateCache.GetBlocks().TryGetValue(blockhash, out var block))
        {
            return block;
        }

        return Repository.GetBlock(blockhash);
    }

    public Vote? GetVote(SHA256Hash votehash)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (StateCache.GetVotes().TryGetValue(votehash, out var vote))
        {
            return vote;
        }

        return Repository.GetVote(votehash);
    }

    public ICollection<Block> GetPendingBlocks()
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetBlocks().Values;
    }

    public ICollection<Vote> GetPendingVotes()
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetVotes().Values;
    }

    public ICollection<Transaction> GetPendingTransactions()
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetTransactions().Values;
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        using var _ = rwlock.EnterReadLockEx();

        var toSkip = pageNum * pageSize;

        var results = StateCache.GetTransactions()
            .Skip(toSkip)
            .Take(pageSize)
            .Select(x => x.Value)
            .ToList();

        toSkip -= results.Count;

        var count = pageSize - results.Count;

        // fill rest from db
        results.AddRange(Repository.GetTransactions(count, toSkip));

        return results;
    }

    public List<Validator> GetValidators()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetValidators();
    }

    public Checkpoint CreateCheckpoint()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.CreateCheckpoint();
    }

    public List<Transaction> GetTransactionsAtHeight(long height)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetTransactionsAtHeight(height);
    }

    public bool LoadStagingChain(string storeName, ChainState newChain, IStateCache newState, List<EventBase> events)
    {
        using var _ = rwlock.EnterWriteLockEx();
        var chainState = Repository.GetChainState();

        if (newChain.Weight <= chainState?.Weight)
        {
            Logger.LogInformation("Discarding staging due to lower weight");
            Repository.DeleteStore(storeName);
            return false;
        }

        Logger.LogInformation("Replacing current chain with staging");
        Repository.ReplaceDbFrom(storeName);

        Logger.LogInformation("Restoring State");

        StateCache.Clear();

        // Add pending transactions from new state
        foreach (var tx in newState.GetTransactions())
        {
            StateCache.Add(tx.Value);
        }

        // Add pending ledgers from new state
        foreach (var ledger in newState.GetLedgers())
        {
            StateCache.Add(ledger.Value);
        }

        StateCache.SetView(newState.GetCurrentView());
        StateCache.SetChainState(newState.GetCurrentState());

        EventBus.Publish(events);

        Logger.LogInformation("Chain restored from staging");
        return true;
    }

    public override void Broadcast(View view)
    {
        BroadcastManager.Broadcast(new ViewBroadcast(view.GetHash(), view.LastHash, StateCache.GetCurrentState().Weight));
    }

    public override void Broadcast(Block block)
    {
        BroadcastManager.Broadcast(new BlockBroadcast(block.GetHash()));
    }

    public override void Broadcast(Vote vote)
    {
        BroadcastManager.Broadcast(new VoteBroadcast(vote.GetHash()));
    }

    public override void Broadcast(Transaction tx)
    {
        BroadcastManager.Broadcast(new TransactionBroadcast(tx.CalculateHash()));
    }

    public override void Publish(EventBase ev)
    {
        EventBus.Publish(ev);
    }

    public override void Publish(List<EventBase> events)
    {
        EventBus.Publish(events);
    }

    public List<Transaction> GetVotesForAddress(Address address, int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetVotesForAddress(address, count);
    }

    public ulong GetEstimatedStakeReward(Address address, long milestoneId)
    {
        var tmpView = new View() { Id = milestoneId };
        var tmpState = new ChainState();
        var transactions = new List<Transaction>();

        using (var _ = rwlock.EnterReadLockEx())
        {
            HandleEpochChange(tmpView, tmpState, transactions);
        }

        return transactions
            .Where(x => x.From == address)
            .Select(x => x.Value)
            .SingleOrDefault();
    }

    public long GetLastHeightContainingBlock()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetLastHeightContainingBlock();
    }

    public ulong GetTransactionFeeEstimate(Transaction tx)
    {
        if (!tx.To.IsContract())
        {
            return (ulong)tx.CalculateFee();
        }

        CallContractMethod(tx, true, out var gasFee);

        // Add 50% extra as the smart contract execution might vary.
        // Might not be enough in all cases...
        return (ulong)(tx.CalculateFee() + Math.Ceiling(gasFee * 1.5d));
    }
}
