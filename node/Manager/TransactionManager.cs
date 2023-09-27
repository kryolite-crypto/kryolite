using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.Search;

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

    public abstract void Broadcast(Transaction tx);
    public abstract void Publish(EventBase ev);
    public abstract string CHAIN_NAME { get; }

    public bool AddGenesis(Genesis genesis, View view)
    {
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

            return AddViewInternal(view, false, false);
        }
        catch (Exception ex)
        {
            LogError(ex, $"{CHAIN_NAME}AddGenesis error");
        }

        return false;
    }

    protected bool AddTransactionBatchInternal(List<TransactionDto> transactionList, bool broadcast, bool castVote)
    {
        var transactions = new ConcurrentDictionary<SHA256Hash, Transaction>(Environment.ProcessorCount, transactionList.Count());

        Parallel.ForEach(transactionList, tx =>
        {
            var hash = tx.CalculateHash();

            switch (tx.TransactionType)
            {
                case TransactionType.BLOCK:
                    var block = new Block(tx, tx.Parents);
                    transactions.TryAdd(hash, block);
                    Logger.LogDebug($"Converted TransactionDto ({hash}) to Transaction ({block.TransactionId})");
                    break;
                case TransactionType.PAYMENT:
                case TransactionType.CONTRACT:
                case TransactionType.REG_VALIDATOR:
                    var transaction = new Transaction(tx, tx.Parents);
                    transactions.TryAdd(hash, transaction);
                    Logger.LogDebug($"Converted TransactionDto ({hash}) to Transaction ({transaction.TransactionId})");
                    break;
                case TransactionType.VIEW:
                    var view = new View(tx, tx.Parents);
                    transactions.TryAdd(hash, view);
                    Logger.LogDebug($"Converted TransactionDto ({hash}) to Transaction ({view.TransactionId})");
                    break;
                case TransactionType.VOTE:
                    var vote = new Vote(tx, tx.Parents);
                    transactions.TryAdd(hash, vote);
                    Logger.LogDebug($"Converted TransactionDto ({hash}) to Transaction ({vote.TransactionId})");
                    break;
                default:
                    LogInformation($"{CHAIN_NAME}Unknown transaction type ({tx.TransactionType})");
                    return;
            }
        });

        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>();

        graph.AddVertexRange(transactions.Values.Select(x => x.CalculateHash()));

        foreach (var tx in transactions)
        {
            foreach (var parent in tx.Value.Parents)
            {
                if (graph.ContainsVertex(parent))
                {
                    graph.AddEdge(new Edge<SHA256Hash>(tx.Key, parent));
                }
            }
        }

        StateCache.EnsureTransactionCapacity(StateCache.TransactionCount() + transactions.Count());
        StateCache.EnsureLedgerCapacity(StateCache.LedgerCount() + transactions.Count());

        Verifier.Verify(transactions.Values);

        bool success = true;

        try
        {
            foreach (var vertex in graph.TopologicalSort().Reverse())
            {
                var tx = transactions[vertex];

                if (tx.ExecutionResult == ExecutionResult.SUCCESS)
                {
                    Logger.LogDebug($"{CHAIN_NAME}{tx.TransactionId} already exists");
                    continue;
                }

                Logger.LogDebug($"{CHAIN_NAME}{tx.TransactionId}");

                // Verify second part, requiring concurrent execution
                if(!Verifier.VerifyTypeOnly(tx, transactions))
                {
                    LogError($"{CHAIN_NAME}{tx.TransactionId} = {tx.ExecutionResult}");
                    success = false;
                    break;
                }

                switch (tx.TransactionType)
                {
                    case TransactionType.BLOCK:
                        success = AddBlockInternal((Block)tx, broadcast);
                        break;
                    case TransactionType.PAYMENT:
                    case TransactionType.CONTRACT:
                        success = AddTransactionInternal(tx, broadcast);
                        break;
                    case TransactionType.VIEW:
                        success = AddViewInternal((View)tx, broadcast, castVote);
                        break;
                    case TransactionType.VOTE:
                        success = AddVoteInternal((Vote)tx, broadcast);
                        break;
                    case TransactionType.REG_VALIDATOR:
                        success = AddValidatorRegInternal(tx, broadcast);
                        break;
                    default:
                        LogInformation($"{CHAIN_NAME}Unknown transaction type ({tx.TransactionType})");
                        break;
                }

                if (!success)
                {
                    LogInformation($"{CHAIN_NAME}Failed to add transaction");
                    success = false;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogError(ex, $"{CHAIN_NAME}AddTransactionBatch error");
            success = false;
        }

        foreach (var txDto in transactionList)
        {
            if (transactions.TryGetValue(txDto.CalculateHash(), out var tx))
            {
                txDto.IsValid = tx.ExecutionResult == ExecutionResult.PENDING;
            }
        }

        return success;
    }

    protected bool AddViewInternal(View view, bool broadcast, bool castVote)
    {
        using var dbtx = Repository.BeginTransaction();

        try
        {
            var sw = Stopwatch.StartNew();

            var height = view.Height ?? 0;

            StateCache.Add(view);

            var bfs = new BreadthFirstSearchAlgorithm<SHA256Hash, Edge<SHA256Hash>>(StateCache.GetPendingGraph());
            bfs.SetRootVertex(view.TransactionId);
            bfs.Compute();

            var toExecute = new List<Transaction>(bfs.VisitedGraph.VertexCount);
            var stalesAndOrphans = new List<Transaction>(bfs.VisitedGraph.VertexCount);
            var voteCount = 0;
            var blockCount = 0;
            var totalStake = 0L;
            var seedStake = 0L;

            var traversedGraph = bfs.VisitedGraph.TopologicalSort()
                .Reverse()
                .ToList();

            foreach (var vertex in traversedGraph)
            {
                // white == not visited
                if (bfs.VerticesColors[vertex] != GraphColor.White)
                {
                    continue;
                }

                // If we did not visit blocks and votes they become stale due to
                // referencing old view
                if (TryHandleStale(vertex, height, out var stale))
                {
                    stalesAndOrphans.Add(stale);
                }
            }

            CleanupOrphans(height, stalesAndOrphans);

            foreach (var tx in stalesAndOrphans)
            {
                // mark orphaned tx not visited
                bfs.VerticesColors[tx.TransactionId] = GraphColor.White;
                
                switch (tx.TransactionType)
                {
                    case TransactionType.BLOCK:
                        {
                            if (StateCache.TryGet(tx.To!, out var to))
                            {
                                to.Pending = checked(to.Pending - tx.Value);
                            }

                            break;
                        }
                    case TransactionType.PAYMENT:
                    {
                        if (StateCache.TryGet(tx.From!, out var from))
                        {
                            from.Balance = checked(from.Balance + tx.Value);
                        }

                        if (StateCache.TryGet(tx.To!, out var to))
                        {
                            to.Pending = checked(to.Pending - tx.Value);
                        }

                        break;
                    }
                    case TransactionType.REG_VALIDATOR:
                    {
                        if (StateCache.TryGet(tx.From!, out var from))
                        {
                            var stake = Repository.GetStake(tx.From!);
                            from.Balance = checked(from.Balance + tx.Value - (stake?.Stake ?? 0));
                        }
                        break;
                    }
                }
            }


            foreach (var vertex in traversedGraph)
            {
                // white == not visited
                if (bfs.VerticesColors[vertex] == GraphColor.White)
                {
                    continue;
                }

                if (TryHandleExecutable(vertex, ref blockCount, ref voteCount, ref seedStake, ref totalStake, out var tx))
                {
                    toExecute.Add(tx);
                }
            }

            var context = new ExecutorContext(Repository, StateCache.GetLedgers(), StateCache.GetCurrentView(), totalStake - seedStake, height);
            var executor = ExecutorFactory.Create(context);

            var lastState = Repository.GetChainState();
            var chainState = StateCache.GetCurrentState();

            executor.Execute(toExecute, chainState.CurrentDifficulty);

            chainState.LastHash = view.TransactionId;
            chainState.Height++;
            chainState.Weight += chainState.CurrentDifficulty.ToWork() * (totalStake / Constant.MIN_STAKE);
            chainState.Votes += voteCount;
            chainState.Transactions += toExecute.Count;
            chainState.Blocks += blockCount;
            chainState.CurrentDifficulty = CalculateDifficulty(chainState.CurrentDifficulty.ToWork(), blockCount);

            Repository.AddRange(stalesAndOrphans);
            Repository.AddRange(toExecute);
            Repository.SaveState(chainState);

            if (broadcast)
            {
                Broadcast(view);
            }
            
            var node = KeyRepository.GetKey();
            var address = node!.PublicKey.ToAddress();
            var shouldVote = castVote && Repository.IsValidator(address);

            if (shouldVote)
            {
                var voteParents = GetParentsForVote(view, toExecute);
                var stake = Repository.GetStake(address);
                var vote = new Vote(node!.PublicKey, view.TransactionId, stake?.Stake ?? 0, voteParents.ToImmutableList());

                vote.Sign(node.PrivateKey);
                vote.ExecutionResult = ExecutionResult.PENDING;

                AddVoteInternal(vote, true);
            }

            Publish(chainState);

            foreach (var ledger in StateCache.GetLedgers().Values)
            {
                if (ledger.Pending == 0)
                {
                    StateCache.GetLedgers().Remove(ledger.Address);
                }

                Publish(ledger);
            }

            foreach (var ev in context.GetEvents())
            {
                Publish(ev);
            }

            dbtx.Commit();

            StateCache.SetView(view);
            StateCache.RecreateGraph();

            sw.Stop();
            LogInformation($"{CHAIN_NAME}Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [Transactions = {toExecute.Count - blockCount - voteCount - 1 /* view count */}] [Blocks = {blockCount}] [Votes = {voteCount}] [Next difficulty = {chainState.CurrentDifficulty}]");

            return true;
        }
        catch (Exception ex)
        {
            LogError(ex, $"{CHAIN_NAME}AddView error");

            StateCache.ClearTransactions();
            StateCache.ClearLedgers();

            StateCache.SetView(Repository.GetLastView()!);
            StateCache.SetChainState(Repository.GetChainState()!);

            dbtx.Rollback();
        }

        return false;
    }

    protected bool AddBlockInternal(Block block, bool broadcast)
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

        LogInformation($"{CHAIN_NAME}Added block #{chainState.Blocks} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

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
                LogInformation($"{CHAIN_NAME}AddTransaction rejected (reason = too low balance)");
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

            checked
            {
                from.Balance = balance - tx.Value;
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

    protected List<SHA256Hash> GetTransactionToValidateInternal()
    {
        var hashes = StateCache.GetPendingGraph().Roots()
            .OrderBy(x => Guid.NewGuid())
            .ToList();

        Logger.LogDebug($"{CHAIN_NAME}Tip has {hashes.Count} / {StateCache.GetPendingGraph().VertexCount} transactions");

        if (hashes.Count < 2)
        {
            foreach (var tx in StateCache.GetTransactions())
            {
                if (tx.Value.ExecutionResult == ExecutionResult.STALE || tx.Value.ExecutionResult == ExecutionResult.ORPHAN)
                {
                    continue;
                }

                if (!hashes.Contains(tx.Value.TransactionId))
                {
                    hashes.Add(tx.Value.TransactionId);
                }

                if (hashes.Count >= 2)
                {
                    return hashes;
                }
            }
        }

        var chainState = Repository.GetChainState();    
        
        if (hashes.Count < 2)
        {
            // we should always point at minimum to previous view
            if (chainState is not null && !hashes.Contains(chainState.LastHash))
            {
                hashes.Add(chainState.LastHash);
            }
        }
        
        if (hashes.Count < 2)
        {
            var nextHashes = Repository.GetTransactionsAtHeight(chainState?.Height ?? 0)
                .ToList();
                
            foreach (var tx in nextHashes)
            {
                if (tx.ExecutionResult == ExecutionResult.STALE || tx.ExecutionResult == ExecutionResult.ORPHAN)
                {
                    continue;
                }

                if (!hashes.Contains(tx.TransactionId))
                {
                    hashes.Add(tx.TransactionId);
                }
                
                if (hashes.Count >= 2 )
                {
                    return hashes;
                }
            }
        }
        
        if (hashes.Count < 2)
        {
            // should never get this far...
            var nextHashes = Repository.GetLastNTransctions(2)
                .Select(x => x.TransactionId)
                .ToList();

            foreach (var hash in nextHashes)
            {
                if (!hashes.Contains(hash))
                {
                    hashes.Add(hash);
                }

                if (hashes.Count >= 2)
                {
                    return hashes;
                }
            }
        }

        return hashes;
    }

    protected List<SHA256Hash> GetTransactionToValidateInternal(int count)
    {
        var hashes = StateCache.GetPendingGraph().Roots()
            .OrderBy(x => Guid.NewGuid())
            .Take(count)
            .ToList();

        if (hashes.Count < count)
        {
            foreach (var tx in StateCache.GetTransactions())
            {
                if (tx.Value.ExecutionResult == ExecutionResult.STALE || tx.Value.ExecutionResult == ExecutionResult.ORPHAN)
                {
                    continue;
                }

                if (!hashes.Contains(tx.Value.TransactionId))
                {
                    hashes.Add(tx.Value.TransactionId);
                }

                if (hashes.Count >= count)
                {
                    return hashes;
                }
            }
        }
        
        var chainState = Repository.GetChainState();    
        
        if (hashes.Count < count)
        {
            // we should always point at minimum to previous view
            if (chainState is not null && !hashes.Contains(chainState.LastHash))
            {
                hashes.Add(chainState.LastHash);
            }
        }
        
        if (hashes.Count < count)
        {
            var transactions = Repository.GetTransactionsAtHeight(chainState?.Height ?? 0)
                .ToList();
                
            foreach (var tx in transactions)
            {
                if (tx.ExecutionResult == ExecutionResult.STALE || tx.ExecutionResult == ExecutionResult.ORPHAN)
                {
                    continue;
                }

                if (!hashes.Contains(tx.TransactionId))
                {
                    hashes.Add(tx.TransactionId);
                }
                
                if (hashes.Count >= count )
                {
                    return hashes;
                }
            }
        }

        if (hashes.Count < count)
        {
            // should never get this far...
            var transactions = Repository.GetLastNTransctions(count);

            foreach (var tx in transactions)
            {
                if (!hashes.Contains(tx.TransactionId))
                {
                    hashes.Add(tx.TransactionId);
                }

                if (hashes.Count >= count)
                {
                    return hashes;
                }
            }
        }

        return hashes;
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

    private bool TryHandleExecutable(SHA256Hash transactionId, ref int blockCount, ref int voteCount, ref long seedStake, ref long totalStake, [NotNullWhen(true)] out Transaction? tx)
    {
        if (!StateCache.Remove(transactionId, out tx))
        {
            return false;
        }

        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK:
                blockCount++;
                break;
            case TransactionType.VOTE:
                voteCount++;

                // Note: votes value must equal to signers stake, this is verified in Verifier
                var stake = tx.Value;

                if (Constant.SEED_VALIDATORS.Contains(tx.From!))
                {
                    stake = Constant.MIN_STAKE;
                    seedStake += stake;
                }

                totalStake += stake;
                break;
        }

        return true;
    }

    private bool TryHandleStale(SHA256Hash transactionId, long height, out Transaction tx)
    {
        tx = StateCache.GetTransactions()[transactionId];

        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK:
            case TransactionType.VOTE:
                tx.Height = height;
                tx.ExecutionResult = ExecutionResult.STALE;

                return StateCache.Remove(tx.TransactionId, out _);
            default:
                return false;
        }
    }

    private void CleanupOrphans(long height, List<Transaction> stalesAndOrphans)
    {
        bool removed = false;

        do foreach (var tx in StateCache.GetTransactions().Values.ToList())
        {
            bool orphaned = false;

            foreach (var parent in tx.Parents)
            {
                if (!StateCache.Contains(parent) && !Repository.Exists(parent))
                {
                    orphaned = true;
                    break;
                }
            }

            if (orphaned && !stalesAndOrphans.Contains(tx))
            {
                tx.Height = height;
                tx.ExecutionResult = ExecutionResult.ORPHAN;

                if (StateCache.Remove(tx.TransactionId, out _))
                {
                    stalesAndOrphans.Add(tx);
                    removed = true;
                }
            }
        } while (removed);
    }

    private List<SHA256Hash> GetParentsForVote(View view, List<Transaction> toExecute)
    {
        // Vote needs to always be child of the view it votes against or it might get stale or executed in wrong order
        var voteParents = new List<SHA256Hash>() { view.TransactionId };

        foreach (var tx in toExecute)
        {
            if (!voteParents.Contains(tx.TransactionId))
            {
                voteParents.Add(tx.TransactionId);
                break;
            }
        }

        if (voteParents.Count >= 2)
        {
            return voteParents;
        }
        
        foreach (var tx in GetTransactionToValidateInternal(2))
        {
            if (!voteParents.Contains(tx))
            {
                voteParents.Add(tx);
                break;
            }
        }

        return voteParents;
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
