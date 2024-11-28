using Kryolite.FastSerializer;
using Kryolite.EventBus;
using Kryolite.Interface;
using Kryolite.Node.Network;
using Kryolite.RocksDb;
using Kryolite.Model;
using Kryolite.Model.Dto;
using Kryolite.Type;
using Kryolite.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Module.SmartContract;

namespace Kryolite.Node;

public class StoreManager : TransactionManager, IStoreManager
{
    private readonly IStoreRepository _repository;
    private readonly IKeyRepository _keyRepository;
    private readonly IVerifier _verifier;
    private IEventBus _eventBus;
    private readonly IStateCache _stateCache;
    private readonly ILogger<StoreManager> _logger;

    public override string CHAIN_NAME => "";

    private static readonly ReaderWriterLockSlim _rwlock = new(LockRecursionPolicy.SupportsRecursion);

    public StoreManager(IStoreRepository repository, IKeyRepository keyRepository, IVerifier verifier, IEventBus eventBus, IVirtualMachineFactory vmFactory, IStateCache stateCache, ILogger<StoreManager> logger) : base(repository, keyRepository, vmFactory, stateCache, logger)
    {
        _repository = repository;
        _keyRepository = keyRepository;
        _verifier = verifier;
        _eventBus = eventBus;
        _stateCache = stateCache;
        _logger = logger;
    }

    public bool BlockExists(SHA256Hash blockhash)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetBlocks().ContainsKey(blockhash) || _repository.BlockExists(blockhash);
    }

    public bool VoteExists(SHA256Hash votehash)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetVotes().ContainsKey(votehash) || _repository.VoteExists(votehash);
    }

    public bool TransactionExists(SHA256Hash hash)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetTransactions().ContainsKey(hash) || _repository.TransactionExists(hash);
    }

    public bool AddView(View view, bool broadcast, bool castVote)
    {
        using var _ = _rwlock.EnterWriteLockEx();

        if (!_verifier.Verify(view))
        {
            return false;
        }

        return AddViewInternal(view, broadcast, castVote);
    }

    public bool AddBlock(Block block, bool broadcast)
    {
        using var _ = _rwlock.EnterWriteLockEx();

        try
        {
            if (!_verifier.Verify(block))
            {
                return false;
            }

            return AddBlockInternal(block, broadcast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public bool AddBlock(BlockTemplate blocktemplate, bool broadcast)
    {
        using var _ = _rwlock.EnterWriteLockEx();

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

            if (!_verifier.Verify(block))
            {
                return false;
            }

            return AddBlockInternal(block, broadcast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public ExecutionResult AddTransaction(TransactionDto txDto, bool broadcast)
    {
        using var _ = _rwlock.EnterWriteLockEx();

        try
        {
            var tx = new Transaction(txDto);

            if (!_verifier.Verify(tx))
            {
                return ExecutionResult.VERIFY_FAILED;
            }

            AddTransactionInternal(tx, broadcast);

            return tx.ExecutionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddTransaction error");
            return ExecutionResult.UNKNOWN;
        }
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = _rwlock.EnterWriteLockEx();

        if (!_verifier.Verify(vote))
        {
            return false;
        }

        return AddVoteInternal(vote, broadcast);
    }

    public List<Block> GetBlocks(List<SHA256Hash> blockhashes)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetBlocks(blockhashes);
    }

    public List<Vote> GetVotes(List<SHA256Hash> votehashes)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetVotes(votehashes);
    }

    public List<Transaction> GetTransactions(List<SHA256Hash> transactionIds)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetTransactions(transactionIds);
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetVotesAtHeight(height);
    }

    public View? GetLastView()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetLastView();
    }

    public BlockTemplate GetBlocktemplate(Address wallet)
    {
        using var _ = _rwlock.EnterReadLockEx();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = _stateCache.GetCurrentState();
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
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetChainState()!;
    }

    public ChainState? GetChainState(long height)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetChainState(height);
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = _rwlock.EnterReadLockEx();

        var chainState = _repository.GetChainState();
        return chainState!.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetCurrentState()?.Id ?? 0;
    }

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetLastNTransctions(address, count);
    }

    public ulong GetBalance(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetWallet(address)?.Balance ?? 0;
    }

    public Validator? GetStake(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetValidator(address);
    }

    public void ResetChain()
    {
        using var _ = _rwlock.EnterWriteLockEx();
        _repository.Reset();
    }

    public Contract? GetContract(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetContract(address);
    }

    public byte[]? GetContractCode(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetContractCode(address);
    }

    public byte[]? GetContractSnapshot(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetLatestSnapshot(address);
    }

    public List<Contract> GetContracts()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetContracts();
    }

    public List<Ledger> GetRichList(int count)
    {
        using var _ = _rwlock.EnterReadLockEx();

        return _repository.GetRichList(count);
    }
    public List<Transaction> GetTransactionsForAddress(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetTransactions(address);
    }

    public Transaction? GetTransactionForHash(SHA256Hash hash)
    {
        using var _ = _rwlock.EnterReadLockEx();

        if (_stateCache.GetTransactions().TryGetValue(hash, out var tx))
        {
            return tx;
        }

        return _repository.GetTransaction(hash);
    }

    public Ledger? GetLedger(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();

        if (_stateCache.GetLedgers().TryGetWallet(address, _repository, out var ledger))
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
        using var _ = _rwlock.EnterReadLockEx();

        var contract = _repository.GetContract(tx.To) ?? throw new Exception(ExecutionResult.INVALID_CONTRACT.ToString());
        var code = _repository.GetContractCode(tx.To);
        var snapshot = _repository.GetLatestSnapshot(tx.To) ?? throw new Exception(ExecutionResult.CONTRACT_SNAPSHOT_MISSING.ToString());
        var balance = _repository.GetWallet(tx.To)?.Balance ?? 0;

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

        // Create standalone VirtualMachine as they are not thread-safe and this only executes in read lock that might allow multiple readers at same time
        var context = new Context(contract, tx, _repository.GetLastView()!, Random.Shared, (long)balance);
        using var vm = new VirtualMachine(code, context, _logger);

        // Restore latest snapshot
        vm.RestoreSnapshot(snapshot);
        vm.Fuel = uint.MaxValue;

        var ret = vm.CallMethod(methodName, call.Params ?? [], out var json);

        gasFee = uint.MaxValue - vm.Fuel;

        return json;
    }

    public Token? GetToken(Address contract, SHA256Hash tokenId)
    {
        using var _ = _rwlock.EnterReadLockEx();

        return _repository.GetToken(contract, tokenId);
    }

    public List<Token> GetTokens(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();

        return _repository.GetTokens(address);
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        using var _ = _rwlock.EnterReadLockEx();

        return _repository.GetContractTokens(contractAddress);
    }

    public View? GetView(long height)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetView(height);
    }

    public View? GetView(SHA256Hash viewHash)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetView(viewHash);
    }

    public Block? GetBlock(SHA256Hash blockhash)
    {
        using var _ = _rwlock.EnterReadLockEx();

        if (_stateCache.GetBlocks().TryGetValue(blockhash, out var block))
        {
            return block;
        }

        return _repository.GetBlock(blockhash);
    }

    public Vote? GetVote(SHA256Hash votehash)
    {
        using var _ = _rwlock.EnterReadLockEx();

        if (_stateCache.GetVotes().TryGetValue(votehash, out var vote))
        {
            return vote;
        }

        return _repository.GetVote(votehash);
    }

    public ICollection<Block> GetPendingBlocks()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetBlocks().Values;
    }

    public ICollection<Vote> GetPendingVotes()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetVotes().Values;
    }

    public ICollection<Transaction> GetPendingTransactions()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _stateCache.GetTransactions().Values;
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        using var _ = _rwlock.EnterReadLockEx();

        var toSkip = pageNum * pageSize;

        var results = _stateCache.GetTransactions()
            .Skip(toSkip)
            .Take(pageSize)
            .Select(x => x.Value)
            .ToList();

        toSkip -= results.Count;

        var count = pageSize - results.Count;

        // fill rest from db
        results.AddRange(_repository.GetTransactions(count, toSkip));

        return results;
    }

    public List<Validator> GetValidators()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetValidators();
    }

    public Checkpoint CreateCheckpoint()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.CreateCheckpoint();
    }

    public List<Transaction> GetTransactionsAtHeight(long height)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetTransactionsAtHeight(height);
    }

    public bool LoadStagingChain(string storeName, ChainState newChain, IStateCache newState, List<IEvent> events)
    {
        using var _ = _rwlock.EnterWriteLockEx();
        var chainState = _repository.GetChainState();

        if (newChain.Weight <= chainState?.Weight)
        {
            _logger.LogInformation("Discarding staging due to lower weight");
            _repository.DeleteStore(storeName);
            return false;
        }

        _logger.LogInformation("Replacing current chain with staging");
        _repository.ReplaceDbFrom(storeName);

        _logger.LogInformation("Restoring State");

        _stateCache.Clear();

        // Add pending transactions from new state
        foreach (var tx in newState.GetTransactions())
        {
            _stateCache.Add(tx.Value);
        }

        // Add pending ledgers from new state
        foreach (var ledger in newState.GetLedgers())
        {
            _stateCache.Add(ledger.Value);
        }

        _stateCache.SetView(newState.GetCurrentView());
        _stateCache.SetChainState(newState.GetCurrentState());

        _eventBus.Publish(events);

        _logger.LogInformation("Chain restored from staging");
        return true;
    }

    public override void Broadcast(View view)
    {
        BroadcastManager.Broadcast(new ViewBroadcast(view.GetHash(), view.LastHash, _stateCache.GetCurrentState().Weight));
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

    public List<Transaction> GetVotesForAddress(Address address, int count)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetVotesForAddress(address, count);
    }

    public ulong GetEstimatedStakeReward(Address address, long milestoneId)
    {
        var tmpView = new View() { Id = milestoneId };
        var tmpState = new ChainState();
        var transactions = new List<Transaction>();

        using (var _ = _rwlock.EnterReadLockEx())
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
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.GetLastHeightContainingBlock();
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

    public bool IsValidator(Address address)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _repository.IsValidator(address);
    }

    public override void Publish(IEvent ev)
    {
        _ = _eventBus.Publish(ev);
    }

    public override void Publish(List<IEvent> ev)
    {
        _ = _eventBus.Publish(ev);
    }
}
