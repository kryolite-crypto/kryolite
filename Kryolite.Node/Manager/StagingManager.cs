using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Procedure;
using Kryolite.Node.Repository;
using Kryolite.Node.Storage;
using Kryolite.Node.Storage.Key;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Type;
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

    private ILoggerFactory _loggerFactory { get; set; }

    public override string CHAIN_NAME => "[STAGING] ";

    private StagingManager(IStoreRepository repository, IKeyRepository keyRepository, IVerifier verifier, IStateCache stateCache, ILoggerFactory loggerFactory) : base(repository, keyRepository, stateCache, loggerFactory.CreateLogger("TransactionManager"))
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Logger = loggerFactory.CreateLogger<StagingManager>();

        _loggerFactory = loggerFactory;
    }

    public static StagingManager Open(string storeName, IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var storePath = Path.Combine(dataDir, $"store.{storeName}");

        var repository = new StoreRepository(storePath);
        var keyRepository = new KeyRepository(configuration);

        var stateCache = new StateCache();
        stateCache.SetChainState(repository.GetChainState() ?? new ChainState());

        var loglevel = configuration.GetValue<string>("loglevel") switch
        {
            "critical" => LogLevel.Critical,
            "error" => LogLevel.Error,
            "warning" => LogLevel.Warning,
            "info" => LogLevel.Information,
            "debug" => LogLevel.Debug,
            "trace" => LogLevel.Trace,
            _ => LogLevel.Warning // only enable warning logs in staging by default
        };

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(loglevel)
                .AddSimpleConsole();
        });

        var verifier = new Verifier(repository, stateCache, loggerFactory.CreateLogger<Verifier>());
        var manager = new StagingManager(repository, keyRepository, verifier, stateCache, loggerFactory);

        return manager;
    }

    public bool LoadView(View view)
    {
        if (!Verifier.Verify(view))
        {
            return false;
        }

        return AddViewInternal(view, false, false);
    }

    private static object _lock = new();

    public bool LoadTransactions(List<TransactionDto> transactions)
    {
        var result = Parallel.ForEach(transactions, (txDto, state) =>
        {
            var tx = new Transaction(txDto);

            if (!Verifier.Verify(tx))
            {
                state.Stop();
            }
        });

        if (!result.IsCompleted)
        {
            return false;
        }

        var span = CollectionsMarshal.AsSpan(transactions);

        foreach (var txDto in span)
        {
            var tx = new Transaction(txDto);

            if (!AddTransactionInternal(tx, false))
            {
                Logger.LogInformation($"Failed to apply transaction {tx.ExecutionResult} (from = {tx.From}, to = {tx.To}, value = {tx.Value})");
                return false;
            }
        }

        return true;
    }

    public bool LoadBlocks(List<Block> blocks)
    {
        var result = Parallel.ForEach(blocks, (block, state) =>
        {
            if (!Verifier.Verify(block))
            {
                state.Stop();
            }

            lock (_lock)
            {
                if (!AddBlockInternal(block, false))
                {
                    state.Stop();
                }
            }
        });

        return result.IsCompleted;
    }

    public bool LoadVotes(List<Vote> votes)
    {
        var result = Parallel.ForEach(votes, (vote, state) =>
        {
            if (!Verifier.Verify(vote))
            {
                state.Stop();
            }

            lock (_lock)
            {
                if (!AddVoteInternal(vote, false))
                {
                    state.Stop();
                }
            }
        });

        return result.IsCompleted;
    }

    public ChainState? GetChainState()
    {
        return Repository.GetChainState();
    }

    public View? GetView(long id)
    {
        return Repository.GetView(id);
    }

    public Transaction? GetTransaction(SHA256Hash id)
    {
        return Repository.GetTransaction(id);
    }

    public List<Validator> GetValidators()
    {
        return Repository.GetValidators();
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

    public override void Publish(List<EventBase> events)
    {
        Events.AddRange(events);
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
        _loggerFactory.Dispose();
        Repository.Close();
    }

    public long GetHeight()
    {
        return GetChainState()?.Id ?? 0;
    }

    public void RollbackTo(long targetHeight)
    {
        var height = GetHeight();
        using var dbtx = Repository.BeginTransaction();

        var ledgers = new WalletCache();
        var contracts = new Dictionary<Address, Contract>();
        var tokens = new Dictionary<(Address, SHA256Hash), Token>();
        var validators = new ValidatorCache();

        for (var i = height; i > targetHeight; i--)
        {
            Logger.LogInformation("Rolling back height {height}", i);

            var view = GetView(i);

            if (view is null)
            {
                continue;
            }

            var rewards = Repository.GetTransactions(view.Rewards);

            foreach (var tx in rewards)
            {
                RollbackTransaction(view, tx, false);
            }

            var transactions = Repository.GetTransactions(view.Transactions);

            foreach (var tx in transactions)
            {
                RollbackTransaction(view, tx, false);
            }

            var scheduled = Repository.GetTransactions(view.ScheduledTransactions);

            foreach (var tx in scheduled)
            {
                RollbackTransaction(view, tx, true);
            }

            Repository.DeleteBlocks(view.Blocks);
            Repository.DeleteVotes(view.Votes);
            Repository.Delete(view);
        }

        Repository.DeleteFromIndexAfterHeight(ChainStateKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(LedgerKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(ContractKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(ContractCodeKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(ContractSnapshotKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(TokenKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(ValidatorKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(TokenIdKey.KeyName, targetHeight);
        Repository.DeleteFromIndexAfterHeight(TokenLedgerKey.KeyName, targetHeight);

        dbtx.Commit();

        StateCache.SetChainState(Repository.GetChainState() ?? new ChainState());
    }

    private void RollbackTransaction(View view, Transaction tx, bool isScheduled)
    {
        Repository.Delete(tx);

        if (isScheduled)
        {
            // If this was scheduled execution, add the transaction back to scheduled index
            Repository.AddDueTransaction(tx);
        }

        var isDue = tx.Timestamp <= view.Timestamp;

        if (!isDue)
        {
            return;
        }

        switch (tx.TransactionType)
        {
            case TransactionType.REGISTER_VALIDATOR:
                Events.Add(new ValidatorDisable(tx.From));
                break;

            case TransactionType.DEREGISTER_VALIDATOR:
                Events.Add(new ValidatorEnable(tx.From));
                break;
        }
    }

    public ITransaction BeginTransaction()
    {
        return Repository.BeginTransaction();
    }
}
