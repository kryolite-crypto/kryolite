using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Logging;
using QuikGraph;
using Redbus.Interfaces;

namespace Kryolite.Node;

public class StoreManager : IStoreManager
{
    private IStoreRepository Repository { get; }
    private IBufferService<TransactionDto, OutgoingTransactionService> TransactionBuffer { get; }
    private IExecutorFactory ExecutorFactory { get; }
    private INetworkManager NetworkManager { get; }
    private IWalletManager WalletManager { get; }
    private IEventBus EventBus { get; }
    private ILogger<StoreManager> Logger { get; }

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private static Dictionary<SHA256Hash, Transaction> PendingCache;
    private static Dictionary<Address, Ledger> LedgerCache;
    private static View CurrentView;
    private static ChainState ChainState;
    private static Wallet Node;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public StoreManager(IStoreRepository repository, IBufferService<TransactionDto, OutgoingTransactionService> transactionBuffer, IExecutorFactory executorFactory, INetworkManager networkManager, IWalletManager walletManager, IEventBus eventBus, ILogger<StoreManager> logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        TransactionBuffer = transactionBuffer ?? throw new ArgumentNullException(nameof(transactionBuffer));
        ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        WalletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (PendingCache is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            PendingCache = new();

            var pending = Repository.GetPending();

            foreach (var parent in pending)
            {
                ref var tx = ref CollectionsMarshal.GetValueRefOrAddDefault(PendingCache, parent.TransactionId, out var existed);

                if (!existed)
                {
                    tx = parent;
                }
            }
        }

        if (LedgerCache is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            LedgerCache = new();
        }

        if (CurrentView is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            CurrentView = Repository.GetLastView()!;
        }

        if (ChainState is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            ChainState = Repository.GetChainState()!;
        }

        if (Node is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            Node = walletManager.GetNodeWallet() ?? throw new ArgumentNullException(nameof(Node));
        }
    }

    public bool Exists(SHA256Hash hash)
    {
        return Repository.Exists(hash);
    }

    public bool AddGenesis(Genesis genesis)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            genesis.TransactionId = genesis.CalculateHash();

            Repository.Add(genesis);

            var chainState = new ChainState
            {
                Id = 0,
                Height = -1,
                LastHash = genesis.TransactionId,
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            Repository.SaveState(chainState);

            ChainState = chainState;

            PendingCache.Add(genesis.TransactionId, genesis);

            dbtx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "AddGenesis error");
        }

        return false;
    }

    public bool AddView(View view, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            var sw = Stopwatch.StartNew();

            var height = view.Height ?? 0;

            if (height != ChainState.Height + 1)
            {
                Logger.LogInformation("Discarding view #{height} (reason = invalid height)", view.Height);
                return false;
            }

            var toExecute = new List<Transaction>(PendingCache.Count);

            if (height > 0)
            {
                var earliest = CurrentView.Timestamp + Constant.HEARTBEAT_INTERVAL;

                if (view.Timestamp < earliest)
                {
                    Logger.LogInformation("Discarding view #{height} (reason = timestamp too early)", view.Height);
                    return false;
                }
            }

            var voteCount = 0;
            var blockCount = 0;

            foreach (var parentId in view.Parents)
            {
                if (!PendingCache.Remove(parentId, out var parent))
                {
                    continue;
                }

                if (parent.Height is not null)
                {
                    continue;
                }

                TraverseTransaction(parent, height, toExecute, ref voteCount, ref blockCount);
            }

            var context = new ExecutorContext(Repository, LedgerCache, voteCount, blockCount);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute);

            ChainState.Weight += ChainState.CurrentDifficulty.ToWork() * voteCount;

            if (height > 0)
            {
                if (blockCount == 0)
                {
                    var work = ChainState.CurrentDifficulty.ToWork();
                    var nextTarget = work / 4 * 3;
                    var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                    ChainState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                }
                else
                {
                    var totalWork = ChainState.CurrentDifficulty.ToWork() * blockCount;

                    ChainState.Weight += totalWork;
                    ChainState.CurrentDifficulty = totalWork.ToDifficulty();
                }
            }

            ChainState.Height++;
            ChainState.LastHash = view.TransactionId;

            view.ExecutionResult = ExecutionResult.SUCCESS;

            Repository.SaveState(ChainState);
            Repository.Add(view);
            Repository.Finalize(toExecute);

            dbtx.Commit();

            CurrentView = view;

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(view));
            }

            EventBus.Publish(ChainState);

            foreach (var ledger in LedgerCache.Values)
            {
                EventBus.Publish(ledger);
            }

            LedgerCache = new(); // note: LedgerCache.Clear() has really bad performance here

            sw.Stop();
            Logger.LogInformation($"Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [Transactions = {toExecute.Count}] [Next difficulty = {ChainState.CurrentDifficulty}]");

            return true;
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "AddView error");

            PendingCache = new();

            var pending = Repository.GetPending();

            foreach (var parent in pending)
            {
                ref var tx = ref CollectionsMarshal.GetValueRefOrAddDefault(PendingCache, parent.TransactionId, out var existed);

                if (!existed)
                {
                    tx = parent;
                }
            }

            CurrentView = Repository.GetLastView()!;
            ChainState = Repository.GetChainState()!;
        }

        return false;
    }

    public bool AddBlock(Block block, bool broadcast)
    {
        var sw = Stopwatch.StartNew();

        if (block.To is null)
        {
            Logger.LogInformation("AddBlock rejected (reason = null to address)");
            return false;
        }

        if (block.Value != Constant.BLOCK_REWARD)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid reward)");
            return false;
        }

        if (!block.VerifyNonce())
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid nonce)");
            return false;
        }

        block.TransactionId = block.CalculateHash();

        if (block.Difficulty != ChainState.CurrentDifficulty)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid difficulty)");
            return false;
        }

        /*if (block.ParentHash != ChainState.LastHash)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid parent hash)");
            return false;
        }*/

        var exists = Repository.Exists(block.TransactionId);

        if (exists)
        {
            Logger.LogInformation("AddBlock rejected (reason = already exists)");
            return false;
        }

        if (!LedgerCache.TryGetValue(block.To, out var to))
        {
            to = Repository.GetWallet(block.To);

            if (to is null)
            {
                to = new Ledger(block.To);
            }

            LedgerCache.Add(block.To, to);
        }

        checked
        {
            to.Pending += block.Value;
        }

        ChainState.Blocks++;

        Repository.UpdateWallet(to);
        Repository.Add(block);
        Repository.SaveState(ChainState);

        sw.Stop();

        Logger.LogInformation($"Added block #{ChainState.Blocks} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

        if (broadcast)
        {
            TransactionBuffer.Add(new TransactionDto(block));
        }

        PendingCache.Add(block.TransactionId, block);

        return true;
    }

    public bool AddBlock(Blocktemplate blocktemplate, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            if (blocktemplate.Validates.Count < 2)
            {
                Logger.LogInformation("AddTransaction rejected (reason = not enought transactions referenced)");
                return false;
            }

            foreach (var txhash in blocktemplate.Validates)
            {
                var tx = Repository.Get(txhash);

                if (tx is null)
                {
                    Logger.LogInformation("AddBlock rejected (reason = unknown transaction reference)");
                    return false;
                }

                if (blocktemplate.Timestamp < tx.Timestamp)
                {
                    Logger.LogInformation("AddBlock rejected (reason = invalid timestamp)");
                    return false;
                }
            }

            var block = new Block(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty, blocktemplate.Validates);

            block.Pow = blocktemplate.Solution;

            var newblock = AddBlock(block, broadcast);

            dbtx.Commit();
            return newblock;
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public bool AddTransaction(Transaction tx, bool broadcast)
    {
        //using var dbtx = Repository.BeginTransaction();
        try
        {
            //var sw = Stopwatch.StartNew();

            if (tx.TransactionType == TransactionType.PAYMENT && tx.Value == 0)
            {
                Logger.LogInformation("AddTransaction rejected (reason = zero payment)");
                return false;
            }

            if (tx.To is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = null 'to' address)");
                return false;
            }

            if (!tx.IsVerified)
            {
                tx.TransactionId = tx.CalculateHash();

                var exists = Repository.Exists(tx.TransactionId);

                if (exists)
                {
                    // no need to do anything, we have this already
                    return true;
                }
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Exists {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Reset();

            if (!LedgerCache.TryGetValue(tx.From!, out var from))
            {
                from = Repository.GetWallet(tx.From!) ?? new Ledger(tx.From!);
                LedgerCache.Add(tx.From!, from);
            }

            if (from.Balance < tx.Value)
            {
                Logger.LogInformation("AddTransaction rejected (reason = too low balance)");
                return false;
            }

            if (!LedgerCache.TryGetValue(tx.To, out var to))
            {
                to = Repository.GetWallet(tx.To) ?? new Ledger(tx.To);
                LedgerCache.Add(tx.To, to);
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Wallets {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            checked
            {
                from.Balance -= tx.Value;
                to.Balance += tx.Value;
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Balance {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            Repository.Add(tx);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Add {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            Repository.UpdateWallets(from, to);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Update {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            PendingCache.Add(tx.TransactionId, tx);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Cache {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            /*EventBus.PublishAsync(from);
            EventBus.PublishAsync(to);

            sw.Stop();
            Logger.LogInformation($"AddTransaction.Publish {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            sw.Restart();*/

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(tx));
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Post {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");

            //dbtx.Commit();
            return true;
        }
        catch (Exception ex) 
        {
            //dbtx.Rollback();
            Logger.LogError(ex, "AddTransaction error");
        }

        return false;
    }

    public bool ExecuteTransaction(Transaction tx, bool broadcast)
    {
        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK:
                return AddBlock((Block)tx, broadcast);
            case TransactionType.PAYMENT:
            case TransactionType.CONTRACT:
                return AddTransaction(tx, broadcast);
            case TransactionType.VIEW:
                if (!AddView((View)tx, broadcast))
                {
                    return false;
                }

                var vote = new Vote(Node.PublicKey, CurrentView.Parents.Take(1).ToList());

                vote.Parents.Add(CurrentView.TransactionId);
                vote.Sign(Node.PrivateKey);

                return AddVote(vote, true);
            case TransactionType.VOTE:
                return AddVote((Vote)tx, broadcast);
            default:
                Logger.LogInformation($"Unknown transaction type ({tx.TransactionType})");
                return false;
        }
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            var exists = Repository.Exists(vote.TransactionId);

            if (exists)
            {
                // we already have this
                return true;
            }

            Repository.Add(vote);

            PendingCache.Add(vote.TransactionId, vote);

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(vote));
            }

            dbtx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "AddVote error.");
        }

        return false;
    }

    private void TraverseTransaction(Transaction transaction, long height, List<Transaction> toExecute, ref int voteCount, ref int blockCount)
    {
        transaction.Height = height;

        toExecute.Add(transaction);

        switch (transaction.TransactionType)
        {
            case TransactionType.VOTE:
                if (!Constant.SEED_VALIDATORS.Contains(transaction.PublicKey!))
                {
                    voteCount++;
                }
                break;
            case TransactionType.BLOCK:
                blockCount++;
                break;
        }

        foreach (var parentId in transaction.Parents)
        {
            if (!PendingCache.Remove(parentId, out var parent))
            {
                continue;
            }

            if (parent.Height is not null)
            {
                continue;
            }

            TraverseTransaction(parent, height, toExecute, ref voteCount, ref blockCount);
        }
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
        return Repository.GetTransactionsToValidate();
    }

    public bool AddTransactionBatch(IEnumerable<TransactionDto> transactionList)
    {
        var sw = Stopwatch.StartNew();
        var transactions = transactionList.Select(tx => {
            switch (tx.TransactionType)
            {
                case TransactionType.BLOCK:
                    return new Block(tx, tx.Parents);
                case TransactionType.PAYMENT:
                case TransactionType.CONTRACT:
                    return new Transaction(tx, tx.Parents);
                case TransactionType.VIEW:
                    return new View(tx, tx.Parents);
                case TransactionType.VOTE:
                    return new Vote(tx, tx.Parents);
                default:
                    throw new Exception($"Unknown transaction type ({tx.TransactionType})");
            }
        }).ToList();
        sw.Stop();
        Logger.LogInformation($"Convert {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");

        sw.Restart();
        Parallel.ForEach(transactions, tx => {
            var exists = Repository.Exists(tx.TransactionId);

            if (exists)
            {
                // no need to do anything, we have this already
                return;
            }

            if (!tx.Verify())
            {
                Logger.LogInformation("AddTransaction rejected (reason = invalid signature)");
                return;
            }

            if (tx.Parents.Distinct().Count() < 2)
            {
                Logger.LogInformation("AddTransaction rejected (reason = not enought unique transactions referenced)");
                return;
            }

            foreach (var txId in tx.Parents)
            {
                var parentTx = Repository.GetTimestamp(txId);

                if (parentTx is null)
                {
                    Logger.LogInformation("AddTransaction rejected (reason = unknown transaction reference)");
                    return;
                }

                if (tx.Timestamp < parentTx)
                {
                    Logger.LogInformation("AddTransaction rejected (reason = invalid timestamp)");
                    return;
                }
            }

            tx.IsVerified = true;
        });
        sw.Stop();
        Logger.LogInformation($"Verify {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");

        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        PendingCache.EnsureCapacity(PendingCache.Count + transactions.Count());
        LedgerCache.EnsureCapacity(LedgerCache.Count + transactions.Count());

        try
        {
            sw.Restart();
            foreach (var tx in transactions)
            {
                if (!tx.IsVerified)
                {
                    continue;
                }

                ExecuteTransaction(tx, true);
            }
            sw.Stop();
            Logger.LogInformation($"Stage {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");

            sw.Restart();
            dbtx.Commit();
            sw.Stop();
            Logger.LogInformation($"Commit {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "AddTransactionBatch error");
            return false;
        }

        return true;
    }

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();
        //using var dbtx = Repository.BeginTransaction();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = Repository.GetChainState();
        var block = new Block(wallet, timestamp, chainState.LastHash, chainState.CurrentDifficulty, Repository.GetTransactionsToValidate());

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

        var chainState = Repository.GetChainState();
        return chainState.Height;
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

    public bool SetChain(AdjacencyGraph<SHA256Hash, TaggedEdge<SHA256Hash, TransactionDto>> chainGraph, long startHeight)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            RollbackChainIfNeeded(startHeight, chainGraph.VertexCount);

            var batch = new List<TransactionDto>(chainGraph.VertexCount);

            foreach (var vertex in chainGraph.Vertices)
            {
                foreach (var edge in chainGraph.OutEdges(vertex).DistinctBy(x => x.Target))
                {

                }
            }

            if (!AddTransactionBatch(batch))
            {
                Logger.LogError($"Set chain failed");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chain reorg failure");
            return false;
        }

        Logger.LogInformation("Chain synchronization completed");
        return true;
    }

    private void RollbackChainIfNeeded(long startHeight, long count)
    {
        using var txContext = Repository.BeginTransaction();

        try
        {
            long progress = 0;

            var ledger = new Dictionary<Address, Ledger>();
            var contracts = new Dictionary<Address, Contract>();

            var chainState = Repository.GetChainState();
            var wallets = WalletManager.GetWallets();

            var min = startHeight;
            var max = chainState?.Height ?? 0;

            if (min > max)
            {
                return;
            }

            // ChainObserver.ReportProgress("Rolling back current chain", progress, count);

            for (long height = max; height >= min; height--)
            {
                var graph = Repository.GetTransactionsAtHeight(height)
                    .AsGraph();

                if (graph.Vertices.Count() == 0)
                {
                    continue;
                }

                foreach (var vertex in graph.Vertices.Reverse())
                {
                    foreach (var edge in graph.OutEdges(vertex).DistinctBy(x => x.Target))
                    {
                        var tx = edge.Tag;

                        if (tx.PublicKey != null)
                        {
                            var addr = tx.PublicKey.ToAddress();

                            if (!ledger.TryGetValue(addr, out var sender))
                            {
                                sender = Repository.GetWallet(addr);

                                if (sender is null)
                                {
                                    continue;
                                }

                                ledger.Add(addr, sender);
                            }

                            checked
                            {
                                sender.Balance += tx.Value;
                            }
                        }

                        if (tx.To is not null)
                        {
                            if (tx.To.IsContract())
                            {
                                if (!contracts.TryGetValue(tx.To, out var contract))
                                {
                                    contract = Repository.GetContract(tx.To);

                                    if (contract is null)
                                    {
                                        continue;
                                    }

                                    contracts.Add(tx.To, contract);
                                }

                                foreach (var effect in tx.Effects)
                                {
                                    if (effect.IsTokenEffect())
                                    {
                                        RollbackTokenEffect(ledger, contract, effect);
                                    }
                                    else
                                    {
                                        RollbackEffectBalance(ledger, contract, effect);
                                    }
                                }

                                Repository.DeleteContractSnapshot(contract.Address, height);

                                if (tx.TransactionType == TransactionType.CONTRACT)
                                {
                                    Repository.DeleteContract(contract.Address);
                                }
                            }
                            else
                            {
                                if (!ledger.TryGetValue(tx.To, out var recipient))
                                {
                                    recipient = Repository.GetWallet(tx.To);

                                    if (recipient is null)
                                    {
                                        continue;
                                    }

                                    ledger.Add(tx.To, recipient);
                                }

                                recipient.Balance = checked(recipient.Balance - tx.Value);
                            }
                        }

                        Repository.Delete(tx);
                    }

                    Repository.DeleteState(height);

                    // ChainObserver.ReportProgress("Rolling back current chain", ++progress, graph.VertexCount);
                }
            }

            var newState = Repository.GetChainStateAt(min - 1);

            if (newState is not null)
            {
                ChainState = newState;
                Repository.SaveState(newState);
            }

            Repository.UpdateWallets(ledger.Values);
            Repository.UpdateContracts(contracts.Values);

            txContext.Commit();
        }
        catch (Exception ex)
        {
            txContext.Rollback();
            throw new Exception("Chain rollback failed", ex);
        }
    }

    public void ResetChain()
    {
        using var _ = rwlock.EnterWriteLockEx();

        // Repository.GetContext().Database.EnsureDeleted();
        // Repository.GetContext().Database.Migrate();
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

        return Repository.Get(hash);
    }

    public Ledger? GetLedger(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

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

        using var vm = KryoVM.LoadFromSnapshot(code, snapshot.Snapshot)
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

    private void RollbackEffectBalance(Dictionary<Address, Ledger> ledger, Contract contract, Effect effect)
    {
        if (!ledger.TryGetValue(effect.To, out var toWallet))
        {
            toWallet = Repository.GetWallet(effect.To);

            if (toWallet is null)
            {
                return;
            }

            ledger.Add(effect.To, toWallet);
        }

        checked
        {
            toWallet.Balance -= effect.Value;
            contract.Balance += effect.Value;
        }
    }

    private void RollbackTokenEffect(Dictionary<Address, Ledger> ledger, Contract contract, Effect effect)
    {
        if (effect.TokenId is null)
        {
            throw new ArgumentNullException("effect.TokenId is null, unable to rollback token");
        }

        if (!ledger.TryGetValue(effect.From, out var fromWallet))
        {
            fromWallet = Repository.GetWallet(effect.From);

            if (fromWallet is null)
            {
                return;
            }

            ledger.Add(effect.From, fromWallet);
        }

        var token = Repository.GetToken(contract.Address, effect.TokenId);

        if (token is null)
        {
            Logger.LogWarning($"Trying to rollback nonexisting token: {effect.TokenId}");
            return;
        }

        if (effect.ConsumeToken)
        {
            token.IsConsumed = false;
            Repository.UpdateToken(token);
            return;
        }

        if (effect.From == contract.Address)
        {
            Repository.DeleteToken(token);
        }
        else
        {
            token.Ledger = fromWallet.Address;
            Repository.UpdateToken(token);
        }
    }

    public View? GetView(SHA256Hash transactionId)
    {
        throw new NotImplementedException();
    }

    public List<Transaction> GetTransactionsAfterHeight(long height)
    {
        throw new NotImplementedException();
    }
}
