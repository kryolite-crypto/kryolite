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

    private StagingManager(IStoreRepository repository, IKeyRepository keyRepository, IVerifier verifier, IStateCache stateCache, IExecutorFactory executorFactory, IEventBus eventBus, ILoggerFactory loggerFactory) : base(repository, keyRepository, verifier, stateCache, executorFactory, loggerFactory.CreateLogger("TransactionManager"))
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
        var eventBus = new EventBus.EventBus(); // TODO: need eventbus that records and replays events

        var stateCache = new StateCache();
        var verifier = new Verifier(repository, stateCache, loggerFactory.CreateLogger<Verifier>());

        return new StagingManager(repository, keyRepository, verifier, stateCache, executor, eventBus, loggerFactory);
    }

    public bool LoadTransactions(List<TransactionDto> transactions)
    {
        return AddTransactionBatchInternal(transactions, false, false);
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
}
