using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
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
using Redbus.Events;
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
            genesis.ExecutionResult = ExecutionResult.PENDING;

            var chainState = new ChainState
            {
                Id = 0,
                Height = -1,
                LastHash = genesis.TransactionId,
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            Repository.AddRange(new() { genesis });
            Repository.SaveState(chainState);

            foreach (var validator in Constant.SEED_VALIDATORS)
            {
                var stake = new Validator
                {
                    NodeAddress = validator
                };
                stake.PushStake(0, new Address());

                Repository.SetStake(validator, stake);
            }

            StateCache.SetChainState(chainState);

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

    public bool AddView(View view, bool broadcast, bool castVote, bool isGenesis = false)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

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
            var seedStake = 0L;

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

                        // Note: votes value must equal to signers stake, this is verified in Verifier
                        var stake = tx.Value;

                        if (Constant.SEED_VALIDATORS.Contains(tx.From!))
                        {
                            stake = Constant.MIN_STAKE;
                            seedStake += stake;
                        }

                        totalStake += stake;
                    }
                }
            }

            var context = new ExecutorContext(Repository, StateCache.GetLedgers(), StateCache.GetCurrentView(), EventBus, totalStake - seedStake, height);
            var executor = ExecutorFactory.Create(context);
            var chainState = StateCache.GetCurrentState();

            executor.Execute(toExecute, chainState.CurrentDifficulty);

            chainState.Weight += chainState.CurrentDifficulty.ToWork() * (totalStake / 1_000_000);

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
            chainState.Votes = voteCount;
            chainState.Transactions = toExecute.Count;

            view.ExecutionResult = ExecutionResult.SUCCESS;

            // cleanup stales and orphans
cleanup:
            bool removed = false;

            foreach (var tx in StateCache.GetTransactions().Values.ToList())
            {
                // white == not visited
                if (bfs.VerticesColors[tx.TransactionId] == GraphColor.White)
                {
                    continue;
                }

                if (tx.TransactionType == TransactionType.BLOCK)
                {
                    if (StateCache.TryGet(tx.To!, out var ledger))
                    {
                        ledger.Pending = checked(ledger.Pending - tx.Value);
                    }

                    tx.ExecutionResult = ExecutionResult.STALE;

                    chainState.Blocks--;

                    StateCache.Remove(tx.TransactionId, out _);
                    toExecute.Add(tx);

                    removed = true;
                }
                else if (tx.TransactionType == TransactionType.VOTE)
                {
                    tx.ExecutionResult = ExecutionResult.STALE;

                    StateCache.Remove(tx.TransactionId, out _);
                    toExecute.Add(tx);

                    removed = true;
                }
                else
                {
                    bool orphaned = false;

                    foreach (var parent in tx.Parents)
                    {
                        if (!StateCache.Contains(parent) && !Repository.Exists(parent))
                        {
                            orphaned = true;
                        }
                    }

                    if (orphaned)
                    {
                        tx.ExecutionResult = ExecutionResult.ORPHAN;

                        StateCache.Remove(tx.TransactionId, out _);
                        toExecute.Add(tx);

                        removed = true;
                    }
                }
            }

            if (removed)
            {
                goto cleanup;
            }

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

                foreach (var tx in toExecute.OrderBy(x => Random.Shared.Next()))
                {
                    if (!parents.Contains(tx.TransactionId))
                    {
                        parents.Add(tx.TransactionId);
                    }

                    if (parents.Count >= 2)
                    {
                        break;
                    }
                }

                if (parents.Count < 2)
                {
                    parents.AddRange(toExecute.OrderBy(x => Random.Shared.Next()).Select(x => x.TransactionId).Take(1));
                }

                var vote = new Vote(node!.PublicKey, view.TransactionId, stake?.Stake ?? 0, parents.ToImmutableList());

                vote.Sign(node.PrivateKey);
                vote.ExecutionResult = ExecutionResult.PENDING;

                AddVoteInternal(vote, true);
            }

            EventBus.Publish(chainState);

            foreach (var ledger in StateCache.GetLedgers().Values)
            {
                /*if (ledger.Pending == 0)
                {
                    StateCache.GetLedgers().Remove(ledger.Address);
                }*/

                EventBus.Publish(ledger);
            }

            foreach (var ev in context.GetEvents())
            {
                EventBus.Publish(ev);
            }

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

        try
        {
            var block = new Block(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty, blocktemplate.Validates, blocktemplate.Solution);

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

        sw.Stop();

        Logger.LogInformation($"Added block #{chainState.Blocks} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

        if (broadcast)
        {
            TransactionBuffer.Add(new TransactionDto(block));
        }

        StateCache.Add(block);

        EventBus.Publish(to);

        return true;
    }

    public ExecutionResult AddTransaction(TransactionDto txDto, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        var tx = txDto.AsTransaction();

        if (!Verifier.Verify(tx))
        {
            return ExecutionResult.VERIFY_FAILED;
        }

        AddTransactionInternal(tx, broadcast);

        return tx.ExecutionResult;
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
                tx.ExecutionResult = ExecutionResult.TOO_LOW_BALANCE;
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
                to.Pending += tx.Value;
            }

            StateCache.Add(tx);

            EventBus.Publish(from);
            EventBus.Publish(to);

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

    public ExecutionResult AddValidatorReg(TransactionDto txDto, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        var tx = txDto.AsTransaction();

        if (!Verifier.Verify(tx))
        {
            return ExecutionResult.VERIFY_FAILED;
        }

        AddValidatorRegInternal(tx, broadcast);

        return tx.ExecutionResult;
    }

    private bool AddValidatorRegInternal(Transaction tx, bool broadcast)
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
                Logger.LogInformation("AddValidatorReg rejected (reason = too low balance)");
                return false;
            }

            checked
            {
                from.Balance = balance - tx.Value;
            }

            StateCache.Add(tx);

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(tx));
            }

            return true;
        }
        catch (Exception ex) 
        {
            Logger.LogError(ex, "AddValidatorReg error");
        }

        return false;
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

        if (transactions.Count < 2)
        {
            foreach (var tx in StateCache.GetTransactions())
            {
                if (!transactions.Contains(tx.Value.TransactionId))
                {
                    transactions.Add(tx.Value.TransactionId);
                }

                if (transactions.Count >= 2)
                {
                    break;
                }
            }
        }

        if (transactions.Count < 2)
        {
            transactions.AddRange(Repository.GetLastNTransctions(2 - transactions.Count)
                .Select(x => x.TransactionId)
                .ToList());
        }

        return transactions;
    }

    public List<SHA256Hash> GetTransactionToValidate(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        
        var hashes = StateCache.GetPendingGraph().Roots()
            .OrderBy(x => Guid.NewGuid())
            .Take(count)
            .ToList();

        if (hashes.Count < count)
        {
            foreach (var tx in StateCache.GetTransactions())
            {
                if (!hashes.Contains(tx.Value.TransactionId))
                {
                    hashes.Add(tx.Value.TransactionId);
                }

                if (hashes.Count >= 2)
                {
                    break;
                }
            }
        }

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
                case TransactionType.REG_VALIDATOR:
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

            foreach (var vertex in chainGraph.TopologicalSort().Reverse())
            {
                var tx = transactions[vertex];

                if (tx.ExecutionResult == ExecutionResult.SUCCESS)
                {
                    Logger.LogDebug($"Skip {tx.TransactionId}");
                    continue;
                }

                // Verify second part, requiring concurrent execution
                if(!Verifier.VerifyTypeOnly(tx, transactions))
                {
                    Logger.LogDebug($"Failed {tx.TransactionId}");
                    return false;
                }

                Logger.LogDebug($"Add {tx.TransactionId}");

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
                    case TransactionType.REG_VALIDATOR:
                        AddValidatorRegInternal(tx, broadcast);
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

        return AddTransactionBatchInternal(graph, transactions, broadcast, true);
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

    public bool SetChain(AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> chainGraph, Dictionary<SHA256Hash, TransactionDto> transactions, long minCommonHeight)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var dbtx = Repository.BeginTransaction();

        try
        {
            RollbackChainIfNeeded(minCommonHeight, chainGraph.VertexCount);

            Logger.LogInformation($"Verifying {transactions.Count} transactions (this could take a while)");

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

    public Validator? GetStake(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetStake(address);
    }

    // TODO: Refactor to smaller methods
    private void RollbackChainIfNeeded(long minCommonHeight, long count)
    {
        // long progress = 0;

        var ledger = new Dictionary<Address, Ledger>();
        var contracts = new Dictionary<Address, Contract>();
        var tokens = new Dictionary<SHA256Hash, Token>();

        var chainState = Repository.GetChainState();
        var wallets = WalletManager.GetWallets();

        var max = chainState?.Height ?? 0;

        if (minCommonHeight >= max)
        {
            return;
        }

        // ChainObserver.ReportProgress("Rolling back current chain", progress, count);
        Logger.LogInformation($"Rollback from {max} to {minCommonHeight}");

        for (long height = max; height > minCommonHeight; height--)
        {
            Logger.LogInformation($"Rollback height {height}");

            var transactions = Repository.GetTransactionsAtHeight(height)
                .ToDictionary(x => x.TransactionId, y => y);

            if (transactions.Count() == 0)
            {
                continue;
            }

            var graph = transactions.Values.ToList().AsGraph();

            foreach (var vertex in graph.TopologicalSort())
            {
                var tx = transactions[vertex];

                var from = tx.From ?? Address.NULL_ADDRESS;
                var to = tx.To ?? Address.NULL_ADDRESS;

                // Assign new Ledgers to avoid null checks in many parts
                var sender = ledger.TryGetWallet(from, Repository) ?? new Ledger();
                var recipient = ledger.TryGetWallet(to, Repository) ?? new Ledger();
                var contract = (to.IsContract() ? contracts.TryGetContract(to, Repository) : null) ?? new Contract();

                switch (tx.TransactionType)
                {
                    case TransactionType.PAYMENT:
                        checked
                        {
                            sender.Balance += tx.Value;
                            recipient.Balance -= tx.Value;
                        }
                    break;
                    case TransactionType.BLOCK:
                        recipient.Balance = checked(recipient.Balance - tx.Value);
                    break;
                    case TransactionType.CONTRACT:
                        sender.Balance = checked(sender.Balance += tx.Value);
                        Repository.DeleteContract(to);
                    break;
                    case TransactionType.REG_VALIDATOR:
                        var stake = Repository.GetStake(from) ?? new Validator { NodeAddress = from };

                        sender.Balance = checked(sender.Balance + stake.Stake);
                        stake.PopStake();
                        sender.Balance = checked (sender.Balance - stake.Stake);

                        if (stake.StakeHistory.Count > 0)
                        {
                            Repository.SetStake(from, stake);

                        }
                        else
                        {
                            Repository.DeleteValidator(from);
                        }

                        EventBus.Publish<EventBase>(stake.Stake >= Constant.MIN_STAKE ? 
                            new ValidatorEnable(from) : 
                            new ValidatorDisable(from)
                        );
                    break;
                }

                if (to.IsContract())
                {
                    contract.Balance = recipient.Balance;
                    Repository.DeleteContractSnapshot(to, height);
                }

                foreach (var effect in tx.Effects)
                {
                    if (effect.IsTokenEffect())
                    {
                        RollbackTokenEffect(ledger, tokens, contract, effect);
                    }
                    else
                    {
                        var effectRecipient = ledger.TryGetWallet(effect.To, Repository);

                        if (effectRecipient is not null)
                        {
                            effectRecipient.Balance = checked(effectRecipient.Balance - effect.Value);
                        }

                        if (effect.To.IsContract())
                        {
                            contract.Balance = checked(contract.Balance + effect.Value);
                        }
                    }
                }

                Repository.Delete(tx);
                Repository.DeleteState(height);

                StateCache.Remove(tx.TransactionId, out var _);

                // ChainObserver.ReportProgress("Rolling back current chain", ++progress, graph.VertexCount);
            }
        }

        var newState = Repository.GetChainStateAt(minCommonHeight) ?? throw new Exception("chainstate not found");
        StateCache.SetChainState(newState);
        Repository.SaveState(newState);

        StateCache.SetView(Repository.GetViewAt(minCommonHeight) ?? throw new Exception("view not found"));

        Logger.LogDebug($"new state = {newState.Height}");
        Logger.LogDebug($"new view = {StateCache.GetCurrentView().TransactionId}");

        Repository.UpdateWallets(ledger.Values);
        Repository.UpdateContracts(contracts.Values);
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

    private void RollbackTokenEffect(Dictionary<Address, Ledger> ledger, Dictionary<SHA256Hash, Token> tokens, Contract contract, Effect effect)
    {
        if (effect.TokenId is null)
        {
            throw new ArgumentNullException("effect.TokenId is null, unable to rollback token");
        }

        var fromWallet = ledger.TryGetWallet(effect.From, Repository);
        var token = tokens.TryGetToken(contract.Address, effect.TokenId, Repository);

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
            token.Ledger = fromWallet!.Address;
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
}
