using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class StagingManager : TransactionManager, IDisposable
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

        if (Directory.Exists(storePath))
        {
            Directory.Delete(storePath, true);
        }

        var repository = new StoreRepository(storePath);
        var keyRepository = new KeyRepository(configuration);

        var executor = new ExecutorFactory(loggerFactory.CreateLogger<ExecutorFactory>());
        var stateCache = new StateCache();
        var verifier = new Verifier(repository, stateCache, loggerFactory.CreateLogger<Verifier>());

        var staging = new StagingManager(repository, keyRepository, verifier, stateCache, executor, loggerFactory);

        staging.DisableLogging();
        BlockchainService.InitializeGenesisBlock(staging, loggerFactory.CreateLogger<StagingManager>());
        staging.EnableLogging();

        return staging;
    }

    public bool LoadView(View view)
    {
        if (!Verifier.Verify(view))
        {
            return false;
        }

        return LoadViewWithoutValidation(view);
    }

    public bool LoadTransactions(List<TransactionDto> transactions)
    {
        var toAdd = new List<Transaction>(transactions.Count);

        foreach (var txDto in transactions)
        {
            var tx = new Transaction(txDto);

            if (!Verifier.Verify(tx))
            {
                return false;
            }

            toAdd.Add(tx);
        }

        return LoadTransactionsWithoutValidation(toAdd);
    }

    public bool LoadBlocks(List<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (!Verifier.Verify(block))
            {
                return false;
            }
        }

        return LoadBlocksWithoutValidation(blocks);
    }

    public bool LoadVotes(List<Vote> votes)
    {
        foreach (var vote in votes)
        {
            if (!Verifier.Verify(vote))
            {
                return false;
            }
        }

        return LoadVotesWithoutValidation(votes);
    }

    public bool LoadViewWithoutValidation(View view)
    {
        var ok = AddViewInternal(view, false, false);
        return ok;
    }

    public bool LoadTransactionsWithoutValidation(List<Transaction> transactions)
    {
        foreach (var tx in transactions)
        {
            if (!AddTransactionInternal(tx, false))
            {
                return false;
            }
        }

        return true;
    }

    public bool LoadBlocksWithoutValidation(List<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (!AddBlockInternal(block, false))
            {
                return false;
            }
        }

        return true;
    }

    public bool LoadVotesWithoutValidation(List<Vote> votes)
    {
        foreach (var vote in votes)
        {
            if (!AddVoteInternal(vote, false))
            {
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

    public override void Broadcast(Block block)
    {

    }

    public override void Broadcast(Vote vote)
    {

    }

    public override void Broadcast(View view)
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

    public void Dispose()
    {
        Repository.Close();
    }
}
