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
using QuikGraph.Algorithms;

namespace Kryolite.Node;

public class StoreManager : TransactionManager, IStoreManager
{
    private IStoreRepository Repository { get; }
    private IBufferService<TransactionDto, OutgoingTransactionService> TransactionBuffer { get; }
    private IEventBus EventBus { get; }
    private IStateCache StateCache { get; }
    private IVerifier Verifier { get; }
    private IServerSentEventsService NotificationService { get; }
    private ILogger<StoreManager> Logger { get; }

    public override string CHAIN_NAME => "";

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    public StoreManager(IStoreRepository repository, IKeyRepository keyRepository, IBufferService<TransactionDto, OutgoingTransactionService> transactionBuffer, IExecutorFactory executorFactory, IWalletManager walletManager, IEventBus eventBus, IStateCache stateCache, IVerifier verifier, IServerSentEventsService notificationService, ILogger<StoreManager> logger) : base(repository, keyRepository, verifier, stateCache, executorFactory, logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        TransactionBuffer = transactionBuffer ?? throw new ArgumentNullException(nameof(transactionBuffer));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        NotificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Exists(SHA256Hash hash)
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.Contains(hash) || Repository.Exists(hash);
    }

    public bool AddView(View view, bool broadcast, bool castVote, bool isGenesis = false)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (isGenesis)
        {
            // skip verifications for genesis height
            view.ExecutionResult = ExecutionResult.PENDING;
        }
        else if (!Verifier.Verify(view))
        {
            return false;
        }

        if (AddViewInternal(view, broadcast, castVote))
        {
            NotificationService.SendEventAsync(view.TransactionId.ToString());
            return true;
        }

        return false;
    }

    public bool AddBlock(Blocktemplate blocktemplate, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        try
        {
            var block = new Block(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty, blocktemplate.Validates, blocktemplate.Solution);

            if (!Verifier.Verify(block))
            {
                return false;
            }

            if (AddBlockInternal(block, broadcast))
            {
                NotificationService.SendEventAsync(block.TransactionId.ToString());
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

        var tx = txDto.AsTransaction();

        if (!Verifier.Verify(tx))
        {
            return ExecutionResult.VERIFY_FAILED;
        }

        if(AddTransactionInternal(tx, broadcast))
        {
            NotificationService.SendEventAsync(tx.TransactionId.ToString());
        }

        return tx.ExecutionResult;
    }

    public ExecutionResult AddValidatorReg(TransactionDto txDto, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        var tx = txDto.AsTransaction();

        if (!Verifier.Verify(tx))
        {
            return ExecutionResult.VERIFY_FAILED;
        }

        if(AddValidatorRegInternal(tx, broadcast))
        {
            NotificationService.SendEventAsync(tx.TransactionId.ToString());
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
            NotificationService.SendEventAsync(vote.TransactionId.ToString());
            return true;
        }

        return false;
    }

    public Genesis? GetGenesis()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetGenesis();
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

    public List<SHA256Hash> GetTransactionToValidate()
    {
        using var _ = rwlock.EnterReadLockEx();
        return GetTransactionToValidateInternal();
    }

    public List<SHA256Hash> GetTransactionToValidate(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        return GetTransactionToValidateInternal(count);
    }

    public bool AddTransactionBatch(List<TransactionDto> transactionList, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (AddTransactionBatchInternal(transactionList, broadcast, true))
        {
            NotificationService.SendEventAsync("BATCH");
            return true;
        }

        return false;
    }

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = Repository.GetChainState() ?? throw new Exception("failed to load chainstate");
        var block = new Block(wallet, timestamp, chainState.LastHash, chainState.CurrentDifficulty, GetTransactionToValidate(2).ToImmutableList(), new SHA256Hash());

        return new Blocktemplate
        {
            Height = chainState.Height,
            To = wallet,
            Difficulty = chainState.CurrentDifficulty,
            ParentHash = block.ParentHash,
            Nonce = block.GetHash(),
            Timestamp = block.Timestamp,
            Validates = block.Parents,
            Data = block.Data ?? Array.Empty<byte>()
        };
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetChainState()!;
    }

    public ChainState? GetChainStateAt(long height)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetChainStateAt(height);
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
        return StateCache.GetCurrentState()?.Height ?? 0;
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

        return Repository.Get(hash);
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

    public View? GetView(SHA256Hash transactionId)
    {
        using var _ = rwlock.EnterReadLockEx();

        var tx = Repository.Get(transactionId);

        if (tx is null)
        {
            return null;
        }

        return new View(tx);
    }

    public ICollection<Transaction> GetPendingTransactions()
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.GetTransactions().Values;
    }

    public List<Transaction> GetTransactionsAfterHeight(long height)
    {
        using var _ = rwlock.EnterReadLockEx();

        var transactions = Repository.GetTransactionsAfterHeight(height);
        transactions.AddRange(StateCache.GetTransactions().Values);
        return transactions;
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        using var _ = rwlock.EnterReadLockEx();

        var results = new List<Transaction>(pageSize);

        var toSkip = pageNum * pageSize;

        var transactions = StateCache.GetPendingGraph().TopologicalSort()
            .Reverse()
            .Skip(toSkip)
            .Take(pageSize);

        foreach (var txId in transactions)
        {
            if (StateCache.GetTransactions().TryGetValue(txId, out var tx))
            {
                results.Add(tx);
            }
        }

        toSkip -= results.Count;

        var count = pageSize - results.Count;

        // fill rest from db
        results.AddRange(Repository.GetTransactions(count, toSkip));

        return results;
    }

    public List<Transaction> GetTransactionsAtHeight(long height)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetTransactionsAtHeight(height)
            .OrderByDescending(x => x.Height)
            .ToList();
    }

    public List<Validator> GetValidators()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetValidators();
    }

    public IDisposable TakeLock()
    {
        return rwlock.EnterWriteLockEx();
    }

    public void LoadStagingChain(string storeName, ChainState newChain, IStateCache stateCache, List<EventBase> events)
    {
        using var _ = rwlock.EnterReadLockEx();
        var chainState = Repository.GetChainState();

        if (newChain.Weight <= chainState?.Weight)
        {
            Logger.LogInformation("Discarding staging due to lower weight");
            return;
        }

        Logger.LogInformation("Replacing current chain with staging");
        Repository.ReplaceDbFrom(storeName);

        Logger.LogInformation("Restoring State");

        var pending = StateCache.GetTransactions();

        StateCache.ClearLedgers();

        // Add pending transactions from new state
        foreach (var tx in stateCache.GetTransactions())
        {
            StateCache.Add(tx.Value);
        }

        // Add pending ledgers from new state
        foreach (var ledger in stateCache.GetLedgers())
        {
            StateCache.Add(ledger.Value);
        }

        StateCache.SetView(stateCache.GetCurrentView());
        StateCache.SetChainState(stateCache.GetCurrentState());

        var toAdd = new List<TransactionDto>();

        // Replay pending transactions from old state
        foreach (var tx in pending)
        {
            if (StateCache.Contains(tx.Value.TransactionId) || Repository.Exists(tx.Value.TransactionId))
            {
                continue;
            }

            var missingParents = false;

            foreach (var parent in tx.Value.Parents)
            {
                if (!StateCache.Contains(parent) && !Repository.Exists(tx.Value.TransactionId))
                {
                    missingParents = true;
                }
            }

            if (!missingParents)
            {
                toAdd.Add(new TransactionDto(tx.Value));
            }
        }

        AddTransactionBatchInternal(toAdd, true, false);

        foreach (var ev in events)
        {
            EventBus.Publish(ev);
        }

        Logger.LogInformation("Chain restored from staging");
    }

    public override void Broadcast(Transaction tx)
    {
        TransactionBuffer.Add(new TransactionDto(tx));
    }

    public override void Publish(EventBase ev)
    {
        EventBus.Publish(ev);
    }
}
