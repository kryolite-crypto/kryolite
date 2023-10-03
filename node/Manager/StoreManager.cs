using System.Collections.Immutable;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class StoreManager : TransactionManager, IStoreManager
{
    private IStoreRepository Repository { get; }
    private IMeshNetwork MeshNetwork { get; }
    private IEventBus EventBus { get; }
    private IStateCache StateCache { get; }
    private IVerifier Verifier { get; }
    private IServerSentEventsService NotificationService { get; }
    private ILogger<StoreManager> Logger { get; }

    public override string CHAIN_NAME => "";

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    public StoreManager(IStoreRepository repository, IKeyRepository keyRepository, IExecutorFactory executorFactory, IMeshNetwork meshNetwork, IEventBus eventBus, IStateCache stateCache, IVerifier verifier, IServerSentEventsService notificationService, ILogger<StoreManager> logger) : base(repository, keyRepository, verifier, stateCache, executorFactory, logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        MeshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        NotificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
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

        if (AddViewInternal(view, broadcast, castVote))
        {
            NotificationService.SendEventAsync("VIEW");
            return true;
        }

        return false;
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

            if (AddBlockInternal(block, broadcast))
            {
                NotificationService.SendEventAsync("BLOCK");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public bool AddBlock(Blocktemplate blocktemplate, bool broadcast)
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

            if (AddBlockInternal(block, broadcast))
            {
                NotificationService.SendEventAsync("BLOCK");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public ExecutionResult AddTransaction(TransactionDto txDto, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        var tx = new Transaction(txDto);

        if (!Verifier.Verify(tx))
        {
            return ExecutionResult.VERIFY_FAILED;
        }

        if(AddTransactionInternal(tx, broadcast))
        {
            NotificationService.SendEventAsync("TRANSACTION");
        }

        return tx.ExecutionResult;
    }

    public ExecutionResult AddValidatorReg(TransactionDto txDto, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        var tx = new Transaction(txDto);

        if (!Verifier.Verify(tx))
        {
            return ExecutionResult.VERIFY_FAILED;
        }

        if(AddValidatorRegInternal(tx, broadcast))
        {
            NotificationService.SendEventAsync("VALIDATOR_REG");
        }

        return tx.ExecutionResult;
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (!Verifier.Verify(vote))
        {
            return false;
        }

        if (AddVoteInternal(vote, broadcast))
        {
            NotificationService.SendEventAsync("VOTE");
            return true;
        }

        return false;
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

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = StateCache.GetCurrentState();
        var block = new Block
        {
            To = wallet,
            Value = Constant.BLOCK_REWARD,
            Timestamp = timestamp,
            LastHash = chainState.LastHash,
            Difficulty = chainState.CurrentDifficulty
        };

        return new Blocktemplate
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

    public long GetBalance(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (address.IsContract())
        {
            return Repository.GetContract(address)?.Balance ?? 0;
        }

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

        if (StateCache.TryGet(address, out var ledger))
        {
            return ledger;
        }

        return Repository.GetWallet(address);
    }
    
    public string? CallContractMethod(Address address, CallMethod call)
    {
        using var _ = rwlock.EnterReadLockEx();

        var contract = Repository.GetContract(address) ?? throw new Exception(ExecutionResult.INVALID_CONTRACT.ToString());

        var snapshot = Repository.GetLatestSnapshot(address);

        if (snapshot == null)
        {
            throw new Exception(ExecutionResult.CONTRACT_SNAPSHOT_MISSING.ToString());
        }

        var methodName = $"{call.Method}";
        var method = contract.Manifest.Methods
            .Where(x => x.Name == methodName)
            .FirstOrDefault();

        if (method == null)
        {
            throw new Exception(ExecutionResult.INVALID_METHOD.ToString());
        }

        if (!method.IsReadonly)
        {
            throw new Exception("only readonly methods can be called without transaction");
        }

        var methodParams = new List<object> { contract.EntryPoint ?? throw new Exception(ExecutionResult.CONTRACT_ENTRYPOINT_MISSING.ToString()) };

        if (call.Params is not null)
        {
            methodParams.AddRange(call.Params);
        }

        var vmContext = new VMContext(contract, new Transaction { To = address }, Random.Shared, Logger);

        var code = Repository.GetContractCode(contract.Address);

        using var vm = KryoVM.LoadFromSnapshot(code, snapshot)
            .WithContext(vmContext);

        Console.WriteLine($"Executing contract {contract.Name}:{call.Method}");
        var ret = vm.CallMethod(methodName, methodParams.ToArray(), out var json);
        Console.WriteLine($"Contract result = {ret}");

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

    public bool LoadStagingChain(string storeName, ChainState newChain, IStateCache newState, List<EventBase> events)
    {
        using var _ = rwlock.EnterWriteLockEx();
        var chainState = Repository.GetChainState();

        if (newChain.Weight <= chainState?.Weight)
        {
            Logger.LogInformation("Discarding staging due to lower weight");
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

        foreach (var ev in events)
        {
            EventBus.Publish(ev);
        }

        Logger.LogInformation("Chain restored from staging");
        return true;
    }

    public override void Broadcast(View view)
    {
        MeshNetwork.BroadcastAsync(new ViewBroadcast(view.GetHash(), view.LastHash, StateCache.GetCurrentState().Weight));
    }

    public override void Broadcast(Block block)
    {
        MeshNetwork.BroadcastAsync(new BlockBroadcast(block.GetHash()));
    }

    public override void Broadcast(Vote vote)
    {
        MeshNetwork.BroadcastAsync(new VoteBroadcast(vote.GetHash()));
    }

    public override void Broadcast(Transaction tx)
    {
        MeshNetwork.BroadcastAsync(new TransactionBroadcast(tx.CalculateHash()));
    }

    public override void Publish(EventBase ev)
    {
        EventBus.Publish(ev);
    }
}
