using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Logging;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.Search;
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

        if (AddViewInternal(view, broadcast))
        {
            dbtx.Commit();
            return true;
        }

        dbtx.Rollback();
        return false;
    }

    public bool AddViewInternal(View view, bool broadcast)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            var height = view.Height ?? 0;

            if (height != ChainState.Height + 1)
            {
                Logger.LogInformation("Discarding view #{height} (reason = invalid height)", view.Height);
                return false;
            }

            if (height > 0)
            {
                var earliest = CurrentView.Timestamp + Constant.HEARTBEAT_INTERVAL;

                if (view.Timestamp < earliest)
                {
                    Logger.LogInformation("Discarding view #{height} (reason = timestamp too early)", view.Height);
                    return false;
                }
            }

            PendingCache.Add(view.TransactionId, view);

            var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>(true, PendingCache.Count, 2);

            graph.AddVertexRange(PendingCache.Keys);

            foreach (var entry in PendingCache)
            {
                foreach (var parent in entry.Value.Parents)
                {
                    if (graph.ContainsVertex(parent))
                    {
                        graph.AddEdge(new Edge<SHA256Hash>(entry.Key, parent));
                    }
                }
            }

            var bfs = new BreadthFirstSearchAlgorithm<SHA256Hash, Edge<SHA256Hash>>(graph);
            bfs.SetRootVertex(view.TransactionId);
            bfs.Compute();

            var toExecute = new List<Transaction>(bfs.VisitedGraph.VertexCount);
            var voteCount = 0;
            var blockCount = 0;

            foreach (var vertex in bfs.VisitedGraph.TopologicalSort().Reverse())
            {
                // white == not visited
                if (bfs.VerticesColors[vertex] == GraphColor.White)
                {
                    continue;
                }

                if (PendingCache.Remove(vertex, out var tx))
                {
                    toExecute.Add(tx);

                    if (tx.TransactionType == TransactionType.BLOCK)
                    {
                        blockCount++;
                    }
                    else if (tx.TransactionType == TransactionType.VOTE)
                    {
                        voteCount++;
                    }
                }
            }

            var context = new ExecutorContext(Repository, LedgerCache, voteCount, blockCount);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute, height);

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
                    ChainState.CurrentDifficulty = totalWork.ToDifficulty();
                }
            }

            ChainState.Height++;
            ChainState.LastHash = view.TransactionId;

            view.ExecutionResult = ExecutionResult.SUCCESS;

            Repository.SaveState(ChainState);
            Repository.Add(view);
            Repository.Finalize(toExecute);

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
            Logger.LogInformation($"Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [Transactions = {toExecute.Count - blockCount - voteCount - 1 /* view count */}] [Blocks = {blockCount}] [Votes = {voteCount}] [Next difficulty = {ChainState.CurrentDifficulty}]");

            return true;
        }
        catch (Exception ex)
        {
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

        if (!block.IsVerified && !block.Verify())
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

            var block = new Block(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty, blocktemplate.Validates, blocktemplate.Solution);

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
        try
        {
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

            checked
            {
                from.Balance -= tx.Value;
                to.Balance += tx.Value;
            }

            Repository.Add(tx);
            Repository.UpdateWallets(from, to);

            PendingCache.Add(tx.TransactionId, tx);

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(tx));
            }

            return true;
        }
        catch (Exception ex) 
        {
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
                return AddViewInternal((View)tx, broadcast);
            case TransactionType.VOTE:
                return AddVoteInternal((Vote)tx, broadcast);
            default:
                Logger.LogInformation($"Unknown transaction type ({tx.TransactionType})");
                return false;
        }
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        if (AddVoteInternal(vote, broadcast))
        {
            dbtx.Commit();
            return true;
        }

        dbtx.Rollback();
        return false;
    }

    private bool AddVoteInternal(Vote vote, bool broadcast)
    {
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

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddVote error.");
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
        return Repository.GetTransactionsToValidate();
    }

    private bool AddTransactionBatchInternal(AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> chainGraph, Dictionary<SHA256Hash, TransactionDto> transactionList, bool broadcast)
    {
        var transactions = new ConcurrentDictionary<SHA256Hash, Transaction>(Environment.ProcessorCount, transactionList.Count());

        Parallel.ForEach(transactionList, entry =>
        {
            var tx = entry.Value;

            switch (tx.TransactionType)
            {
                case TransactionType.BLOCK:
                    transactions.TryAdd(entry.Key, new Block(tx, tx.Parents));
                    break;
                case TransactionType.PAYMENT:
                case TransactionType.CONTRACT:
                    transactions.TryAdd(entry.Key, new Transaction(tx, tx.Parents));
                    break;
                case TransactionType.VIEW:
                    transactions.TryAdd(entry.Key, new View(tx, tx.Parents));
                    break;
                case TransactionType.VOTE:
                    transactions.TryAdd(entry.Key, new Vote(tx, tx.Parents));
                    break;
                default:
                    Logger.LogInformation($"Unknown transaction type ({tx.TransactionType})");
                    return;
            }
        });

        transactionList.Clear();

        Parallel.ForEach(transactions, tx =>
        {
            var exists = Repository.Exists(tx.Key);

            if (exists)
            {
                // no need to do anything, we have this already
                return;
            }

            if (!tx.Value.Verify())
            {
                Logger.LogInformation("AddTransaction rejected (reason = invalid signature)");
                return;
            }

            if (tx.Value.Parents.Distinct().Count() < 2)
            {
                Logger.LogInformation("AddTransaction rejected (reason = not enought unique transactions referenced)");
                return;
            }

            /*foreach (var txId in tx.Parents)
            {
                var parentExists = PendingCache.ContainsKey(txId) || Repository.Exists(txId);

                if (!parentExists)
                {
                    Logger.LogInformation("AddTransaction rejected (reason = unknown transaction reference)");
                    return;
                }
            }*/

            tx.Value.IsVerified = true;
        });

        PendingCache.EnsureCapacity(PendingCache.Count + transactionList.Count());
        LedgerCache.EnsureCapacity(LedgerCache.Count + transactionList.Count());

        try
        {
            foreach (var vertex in chainGraph.TopologicalSort().Reverse())
            {
                var tx = transactions[vertex];

                if (!tx.IsVerified)
                {
                    continue;
                }

                ExecuteTransaction(tx, broadcast);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddTransactionBatch error");
            return false;
        }

        return true;
    }

    public bool AddTransactionBatch(List<TransactionDto> transactionList)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>();
        var transactions = new Dictionary<SHA256Hash, TransactionDto>();

        graph.AddVertexRange(transactionList.Select(x => x.CalculateHash()));

        foreach (var tx in transactionList)
        {
            var hash = tx.CalculateHash();
            transactions.Add(hash, tx);

            foreach (var parent in tx.Parents)
            {
                if (graph.ContainsVertex(parent))
                {
                    graph.AddEdge(new Edge<SHA256Hash>(hash, parent));
                }
            }
        }

        if (AddTransactionBatchInternal(graph, transactions, true))
        {
            dbtx.Commit();
            return true;
        }

        dbtx.Rollback();
        return false;
    }

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();
        //using var dbtx = Repository.BeginTransaction();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = Repository.GetChainState();
        var block = new Block(wallet, timestamp, chainState.LastHash, chainState.CurrentDifficulty, Repository.GetTransactionsToValidate(), new SHA256Hash());

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

    public bool SetChain(AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> chainGraph, Dictionary<SHA256Hash, TransactionDto> transactions, long startHeight)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            RollbackChainIfNeeded(startHeight, chainGraph.VertexCount);

            /*var batch = new List<TransactionDto>(chainGraph.VertexCount);

            var lastView = transactions.Values
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .MaxBy(x => BitConverter.ToInt64(x.Data));

            var bfs = new BreadthFirstSearchAlgorithm<SHA256Hash, Edge<SHA256Hash>>(chainGraph);
            bfs.SetRootVertex(lastView!.CalculateHash());
            bfs.Compute();

            foreach (var vertex in bfs.VisitedGraph.TopologicalSort().Reverse())
            {
                if (transactions.Remove(vertex, out var tx))
                {
                    batch.Add(tx);
                }
            }*/

            if (!AddTransactionBatchInternal(chainGraph, transactions, false))
            {
                dbtx.Rollback();
                Logger.LogError($"Set chain failed");
                return false;
            }
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "Chain reorg failure");
            return false;
        }

        dbtx.Commit();
        Logger.LogInformation("Chain synchronization completed");
        return true;
    }

    private void RollbackChainIfNeeded(long startHeight, long count)
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
            var transactions = Repository.GetTransactionsAtHeight(height)
                .ToDictionary(x => x.TransactionId, y => y);

            if (transactions.Count() == 0)
            {
                continue;
            }

            var graph = transactions.Values.ToList().AsGraph();

            var view = transactions
                .Where(x => x.Value.TransactionType == TransactionType.VIEW)
                .Select(x => x.Value)
                .Single();

            var bfs = new BreadthFirstSearchAlgorithm<SHA256Hash, Edge<SHA256Hash>>(graph);
            bfs.SetRootVertex(view.TransactionId);
            bfs.Compute();

            foreach (var vertex in bfs.VisitedGraph.TopologicalSort().Reverse())
            {
                var tx = transactions[vertex];

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
                Repository.DeleteState(height);

                // ChainObserver.ReportProgress("Rolling back current chain", ++progress, graph.VertexCount);
            }
        }

        var newState = Repository.GetChainStateAt(min - 1) ?? throw new Exception("view not found");
        ChainState = newState;
        Repository.SaveState(newState);

        CurrentView = Repository.GetViewAt(min - 1) ?? throw new Exception("view not found");

        Repository.UpdateWallets(ledger.Values);
        Repository.UpdateContracts(contracts.Values);
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
        using var _ = rwlock.EnterReadLockEx();

        var tx = Repository.Get(transactionId);

        if (tx is null)
        {
            return null;
        }

        return new View(tx);
    }

    public List<Transaction> GetTransactionsAfterHeight(long height)
    {
        return Repository.GetTransactionsAfterHeight(height);
    }
}
