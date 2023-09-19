using System.Collections.Concurrent;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QuikGraph;
using QuikGraph.Algorithms;
using Zeroconf;

namespace Kryolite.Node;

public class StagingManager : TransactionManager
{
    public IStoreRepository Repository { get; }
    public ILogger<StagingManager> Logger { get; }
    public IStateCache StateCache { get; }
    public IVerifier Verifier { get; set; }
    public List<EventBase> Events { get; } = new();

    public override string CHAIN_NAME => "[STAGING] ";

    private StagingManager(IStoreRepository repository, IKeyRepository keyRepository, IVerifier verifier, IStateCache stateCache, IExecutorFactory executorFactory, ILoggerFactory loggerFactory) : base(repository, keyRepository, verifier, stateCache, executorFactory, loggerFactory.CreateLogger("TransactionManager"))
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Logger = loggerFactory.CreateLogger<StagingManager>();
    }

    public static StagingManager Create(string storeName, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var dataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var storePath = Path.Combine(dataDir, $"store.{storeName}");
        var repository = new StoreRepository(storePath);
        var keyRepository = new KeyRepository(configuration);

        var executor = new ExecutorFactory(loggerFactory.CreateLogger<ExecutorFactory>());
        var stateCache = new StateCache();
        var verifier = new Verifier(repository, stateCache, loggerFactory.CreateLogger<Verifier>());

        return new StagingManager(repository, keyRepository, verifier, stateCache, executor, loggerFactory);
    }

    public bool LoadTransactions(List<TransactionDto> transactions)
    {
        return AddTransactionBatchInternal(transactions, false, false);
    }

    public bool LoadTransactionsWithoutValidation(List<Transaction> transactionList)
    {
        var transactions = transactionList.ToDictionary(x => x.TransactionId, x => x);
        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>();

        graph.AddVertexRange(transactionList.Select(x => x.TransactionId));

        foreach (var tx in transactionList)
        {
            foreach (var parent in tx.Parents)
            {
                if (graph.ContainsVertex(parent))
                {
                    graph.AddEdge(new Edge<SHA256Hash>(tx.TransactionId, parent));
                }
            }
        }

        foreach (var vertex in graph.TopologicalSort().Reverse())
        {
            var tx = transactions[vertex];

            bool success = false;

            switch (tx.TransactionType)
            {
                case TransactionType.BLOCK:
                    success = AddBlockInternal(new Block(tx), false);
                    break;
                case TransactionType.PAYMENT:
                case TransactionType.CONTRACT:
                    success = AddTransactionInternal(tx, false);
                    break;
                case TransactionType.VIEW:
                    success = AddViewInternal(new View(tx), false, false);
                    break;
                case TransactionType.VOTE:
                    success = AddVoteInternal(new Vote(tx), false);
                    break;
                case TransactionType.REG_VALIDATOR:
                    success = AddValidatorRegInternal(tx, false);
                    break;
                default:
                    Logger.LogInformation($"{CHAIN_NAME}Unknown transaction type ({tx.TransactionType})");
                    break;
            }

            if (!success)
            {
                Logger.LogInformation($"{CHAIN_NAME}Failed to add transaction");
                return false;
            }
        }

        return true;
    }

    public ChainState? GetChainState()
    {
        return Repository.GetChainState();
    }

    public override void Broadcast(Transaction tx)
    {

    }

    public override void Publish(EventBase ev)
    {
        Events.Add(ev);
    }

    public void DisableLogging()
    {
        loggingDisabled = true;
    }

    public void EnableLogging()
    {
        loggingDisabled = false;
    }
}
