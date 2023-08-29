using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Kryolite.Node.Blockchain;
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
    public IKeyRepository KeyRepository { get; }
    private IBufferService<TransactionDto, OutgoingTransactionService> TransactionBuffer { get; }
    private IExecutorFactory ExecutorFactory { get; }
    private INetworkManager NetworkManager { get; }
    private IWalletManager WalletManager { get; }
    private IEventBus EventBus { get; }
    public IStateCache StateCache { get; }
    public IVerifier Verifier { get; }
    private ILogger<StoreManager> Logger { get; }

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    public StoreManager(IStoreRepository repository, IKeyRepository keyRepository, IBufferService<TransactionDto, OutgoingTransactionService> transactionBuffer, IExecutorFactory executorFactory, INetworkManager networkManager, IWalletManager walletManager, IEventBus eventBus, IStateCache stateCache, IVerifier verifier, ILogger<StoreManager> logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        KeyRepository = keyRepository ?? throw new ArgumentNullException(nameof(keyRepository));
        TransactionBuffer = transactionBuffer ?? throw new ArgumentNullException(nameof(transactionBuffer));
        ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        WalletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Exists(SHA256Hash hash)
    {
        using var _ = rwlock.EnterReadLockEx();
        return StateCache.Contains(hash) || Repository.Exists(hash);
    }

    public bool AddGenesis(Genesis genesis)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            genesis.TransactionId = genesis.CalculateHash();

            var chainState = new ChainState
            {
                Id = 0,
                Height = -1,
                LastHash = genesis.TransactionId,
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            Repository.SaveState(chainState);

            StateCache.SetChainState(chainState);
            StateCache.Add(genesis);

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

    public bool AddView(View view, bool broadcast, bool castVote)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        if (view.Height > 0 && !Verifier.Verify(view))
        {
            return false;
        }

        if (AddViewInternal(view, broadcast, castVote))
        {
            dbtx.Commit();
            return true;
        }

        dbtx.Rollback();
        return false;
    }

    private bool AddViewInternal(View view, bool broadcast, bool castVote)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            var height = view.Height ?? 0;

            StateCache.Add(view);

            var bfs = new BreadthFirstSearchAlgorithm<SHA256Hash, Edge<SHA256Hash>>(StateCache.GetPendingGraph());
            bfs.SetRootVertex(view.TransactionId);
            bfs.Compute();

            var toExecute = new List<Transaction>(bfs.VisitedGraph.VertexCount);
            var voteCount = 0;
            var blockCount = 0;
            var totalStake = 0L;

            foreach (var vertex in bfs.VisitedGraph.TopologicalSort().Reverse())
            {
                // white == not visited
                if (bfs.VerticesColors[vertex] == GraphColor.White)
                {
                    continue;
                }

                if (StateCache.Remove(vertex, out var tx))
                {
                    toExecute.Add(tx);

                    if (tx.TransactionType == TransactionType.BLOCK)
                    {
                        blockCount++;
                    }
                    else if (tx.TransactionType == TransactionType.VOTE)
                    {
                        voteCount++;
                        totalStake += tx.Value;
                    }
                }
            }

            var context = new ExecutorContext(Repository, StateCache.GetLedgers(), totalStake);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute, height);

            var chainState = StateCache.GetCurrentState();
            chainState.Weight += chainState.CurrentDifficulty.ToWork() * totalStake;

            if (height > 0)
            {
                if (blockCount == 0)
                {
                    var work = chainState.CurrentDifficulty.ToWork();
                    var nextTarget = work / 4 * 3;
                    var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                    chainState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                }
                else
                {
                    var totalWork = chainState.CurrentDifficulty.ToWork() * blockCount;
                    chainState.CurrentDifficulty = totalWork.ToDifficulty();
                }
            }

            chainState.Height++;
            chainState.LastHash = view.TransactionId;

            view.ExecutionResult = ExecutionResult.SUCCESS;

            Repository.AddRange(toExecute);
            Repository.SaveState(chainState);

            StateCache.SetView(view);
            StateCache.RecreateGraph();

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(view));
            }

            var node = KeyRepository.GetKey();
            var address = node!.PublicKey.ToAddress();

            if (castVote && Repository.IsValidator(address))
            {
                var stake = Repository.GetStake(address);
                var parents = new List<SHA256Hash>();

                parents.Add(view.TransactionId);

                var vote = new Vote(node!.PublicKey, view.TransactionId, stake, parents);

                vote.Sign(node.PrivateKey);

                AddVoteInternal(vote, true);
            }

            EventBus.Publish(chainState);

            foreach (var ledger in StateCache.GetLedgers().Values)
            {
                EventBus.Publish(ledger);
            }

            StateCache.ClearLedgers();

            sw.Stop();
            Logger.LogInformation($"Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [Transactions = {toExecute.Count - blockCount - voteCount - 1 /* view count */}] [Blocks = {blockCount}] [Votes = {voteCount}] [Next difficulty = {chainState.CurrentDifficulty}]");

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddView error");

            StateCache.ClearTransactions();
            StateCache.ClearLedgers();

            StateCache.SetView(Repository.GetLastView()!);
            StateCache.SetChainState(Repository.GetChainState()!);
        }

        return false;
    }

    public bool AddBlock(Blocktemplate blocktemplate, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            var block = new Block(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty, blocktemplate.Validates, blocktemplate.Solution);

            if (!Verifier.Verify(block))
            {
                return false;
            }

            if (AddBlockInternal(block, broadcast))
            {
                dbtx.Commit();
                return true;
            }

            dbtx.Rollback();
            return false;
        }
        catch (Exception ex)
        {
            dbtx.Rollback();
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    private bool AddBlockInternal(Block block, bool broadcast)
    {
        var sw = Stopwatch.StartNew();
        
        if (!StateCache.TryGet(block.To!, out var to))
        {
            to = Repository.GetWallet(block.To!);

            if (to is null)
            {
                to = new Ledger(block.To!);
            }

            StateCache.Add(to);
        }

        checked
        {
            to.Pending += block.Value;
        }

        var chainState = StateCache.GetCurrentState();

        chainState.Blocks++;

        Repository.UpdateWallet(to);
        Repository.SaveState(chainState);

        sw.Stop();

        Logger.LogInformation($"Added block #{chainState.Blocks} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

        if (broadcast)
        {
            TransactionBuffer.Add(new TransactionDto(block));
        }

        StateCache.Add(block);

        return true;
    }

    public bool AddTransaction(TransactionDto txDto, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        var tx = txDto.AsTransaction();

        if (!Verifier.Verify(tx))
        {
            return false;
        }

        if (AddTransactionInternal(tx, broadcast))
        {
            dbtx.Commit();
            return true;
        }

        dbtx.Rollback();
        return false;
    }

    private bool AddTransactionInternal(Transaction tx, bool broadcast)
    {
        try
        {
            if (!StateCache.TryGet(tx.From!, out var from))
            {
                from = Repository.GetWallet(tx.From!) ?? new Ledger(tx.From!);
                StateCache.Add(from);
            }

            if (from.Balance < tx.Value)
            {
                Logger.LogInformation("AddTransaction rejected (reason = too low balance)");
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
                to.Balance += tx.Value;
            }

            Repository.UpdateWallets(from, to);

            StateCache.Add(tx);

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

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        if (!Verifier.Verify(vote))
        {
            return false;
        }

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
            vote.TransactionId = vote.CalculateHash();

            var exists = Repository.Exists(vote.TransactionId);

            if (exists)
            {
                // we already have this
                return true;
            }

            StateCache.Add(vote);

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
        var transactions = StateCache.GetPendingGraph().Roots()
            .ToList();

        Logger.LogDebug($"Tip has {transactions.Count} / {StateCache.GetPendingGraph().VertexCount} transactions");

        if (transactions.Count == 0)
        {
            return Repository.GetLastNTransctions(1)
                .Select(x => x.TransactionId)
                .ToList();
        }

        return transactions;
    }

    public List<SHA256Hash> GetTransactionToValidate(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        
        var hashes = StateCache.GetPendingGraph().Roots()
            .Take(count)
            .ToList();

        if (hashes.Count < count)
        {
            var transactions = Repository.GetLastNTransctions(count);

            foreach (var tx in transactions)
            {
                if (!hashes.Contains(tx.TransactionId))
                {
                    hashes.Add(tx.TransactionId);
                }

                if (hashes.Count == count)
                {
                    break;
                }
            }
        }

        return hashes;
    }

    private bool AddTransactionBatchInternal(AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> chainGraph, Dictionary<SHA256Hash, TransactionDto> transactionList, bool broadcast, bool castVote)
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

        StateCache.EnsureTransactionCapacity(StateCache.TransactionCount() + transactionList.Count());
        StateCache.EnsureLedgerCapacity(StateCache.LedgerCount() + transactionList.Count());

        Verifier.Verify(transactions.Values);

        try
        {
            Logger.LogDebug($"Executing {transactions.Count} transactions");

            foreach (var vertex in chainGraph.TopologicalSort().Reverse())
            {
                var tx = transactions[vertex];

                if (tx.ExecutionResult != ExecutionResult.VERIFIED)
                {
                    continue;
                }

                switch (tx.TransactionType)
                {
                    case TransactionType.BLOCK:
                        AddBlockInternal((Block)tx, broadcast);
                        break;
                    case TransactionType.PAYMENT:
                    case TransactionType.CONTRACT:
                        AddTransactionInternal(tx, broadcast);
                        break;
                    case TransactionType.VIEW:
                        AddViewInternal((View)tx, broadcast, castVote);
                        break;
                    case TransactionType.VOTE:
                        AddVoteInternal((Vote)tx, broadcast);
                        break;
                    default:
                        Logger.LogInformation($"Unknown transaction type ({tx.TransactionType})");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddTransactionBatch error");
            return false;
        }

        return true;
    }

    public bool AddTransactionBatch(List<TransactionDto> transactionList, bool broadcast)
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

        Logger.LogDebug($"Incoming batch has {transactionList.Count} transactions. Graph has {graph.VertexCount} vertices.");

        if (AddTransactionBatchInternal(graph, transactions, broadcast, true))
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

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = Repository.GetChainState() ?? throw new Exception("failed to load chainstate");
        var block = new Block(wallet, timestamp, chainState.LastHash, chainState.CurrentDifficulty, GetTransactionToValidate(2), new SHA256Hash());

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

            if (!AddTransactionBatchInternal(chainGraph, transactions, false, false))
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

                StateCache.Remove(tx.TransactionId, out var _);

                // ChainObserver.ReportProgress("Rolling back current chain", ++progress, graph.VertexCount);
            }
        }

        var newState = Repository.GetChainStateAt(min - 1) ?? throw new Exception("view not found");
        StateCache.SetChainState(newState);
        Repository.SaveState(newState);

        StateCache.SetView(Repository.GetViewAt(min - 1) ?? throw new Exception("view not found"));

        Repository.UpdateWallets(ledger.Values);
        Repository.UpdateContracts(contracts.Values);
    }

    public void ResetChain()
    {
        using var _ = rwlock.EnterWriteLockEx();

        throw new NotImplementedException();
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
        using var _ = rwlock.EnterReadLockEx();

        var transactions = Repository.GetTransactionsAfterHeight(height);
        transactions.AddRange(StateCache.GetTransactions().Values);
        return transactions;
    }
}
