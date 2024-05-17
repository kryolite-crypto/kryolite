using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Procedure;
using Kryolite.Node.Repository;
using Kryolite.Node.Storage;
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
                .AddConsole();
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
                return false;
            }
        }

        return result.IsCompleted;
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
        var transfer = new Transfer(Repository, ledgers, validators);

        for (var i = height; i > targetHeight; i--)
        {
            Logger.LogInformation("Rolling back height {height}", i);

            var view = GetView(i);

            if (view is null)
            {
                continue;
            }

            var rewards = Repository.GetTransactions(view.Rewards);

            // Rollback in reverse order
            rewards.Reverse();

            foreach (var tx in rewards)
            {
                if (tx.ExecutionResult == ExecutionResult.SUCCESS)
                {
                    RollbackTransaction(view, tx, ref transfer, contracts, tokens, false);
                }
            }

            var transactions = Repository.GetTransactions(view.Transactions);

            // Rollback in reverse order
            transactions.Reverse();

            foreach (var tx in transactions)
            {
                RollbackTransaction(view, tx, ref transfer, contracts, tokens, false);
            }

            var scheduled = Repository.GetTransactions(view.ScheduledTransactions);

            // Rollback in reverse order
            scheduled.Reverse();

            foreach (var tx in scheduled)
            {
                RollbackTransaction(view, tx, ref transfer, contracts, tokens, true);
            }

            Repository.DeleteBlocks(view.Blocks);
            Repository.DeleteVotes(view.Votes);
            Repository.Delete(view);
            Repository.DeleteState(i);
        }

        Repository.UpdateWallets(ledgers.Values);
        Repository.UpdateContracts(contracts.Values);
        Repository.UpdateTokens(tokens.Values);

        foreach (var validator in validators.Values)
        {
            Repository.SetStake(validator.NodeAddress, validator);
        }

        dbtx.Commit();

        StateCache.SetChainState(Repository.GetChainState() ?? new ChainState());
    }

    private void RollbackTransaction(View view, Transaction tx, ref Transfer transfer, Dictionary<Address, Contract> contracts, Dictionary<(Address, SHA256Hash), Token> tokens, bool isScheduled)
    {
        Repository.Delete(tx);

        if (tx.To.IsContract())
        {
            Repository.DeleteContractSnapshot(tx.To, view.Id);
        }

        if (tx.ExecutionResult != ExecutionResult.SUCCESS)
        {
            // failed contract execution has already refunded this tx
            return;
        }

        var isDue = tx.Timestamp <= view.Timestamp;

        if (tx.To.IsContract())
        {
            // Note: effects need to be rolled back before transaction
            foreach (var effect in tx.Effects)
            {
                transfer.From(effect.To, effect.Value, out _, out _);
                transfer.To(effect.Contract, effect.Value, out _);
                RollbackToken(effect, tokens);
            }
        }

        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK_REWARD:
            case TransactionType.STAKE_REWARD:
            case TransactionType.DEV_REWARD:
                if (!transfer.From(tx.To, tx.Value, out var res2, out var ledger2))
                {
                    throw new Exception($"Negative balance after rollback: From = {tx.To}, Balance = {ledger2?.Balance}, Value = {tx.Value}, Result = {res2}");
                }
                break;
            case TransactionType.PAYMENT:
                if (isDue)
                {
                    if (!transfer.From(tx.To, tx.Value, out var res, out var ledger))
                    {
                        throw new Exception($"Negative balance after rollback: From = {tx.To}, Balance = {ledger?.Balance}, Value = {tx.Value}, Result = {res}");
                    }
                }

                if (isScheduled)
                {
                    // If this was scheduled execution, add the transaction back to scheduled index
                    Repository.AddDueTransaction(tx);
                }
                else
                {
                    var total = tx.Value + tx.SpentFee;
                    transfer.To(tx.From, total, out ledger2);
                }

                break;
            case TransactionType.CONTRACT:
                if (isDue)
                {
                    var payload = Serializer.Deserialize<TransactionPayload>(tx.Data);

                    if (payload?.Payload is not NewContract newContract)
                    {
                        break;
                    }

                    var contract = Contract.ToAddress(tx.From!, newContract.Code);

                    transfer.From(contract, tx.Value, out _, out _);

                    Repository.DeleteContractCode(contract);
                    Repository.DeleteContract(contract);

                    contracts.Remove(contract);
                }

                // If this was scheduled execution, add the transaction back to scheduled index
                if (isScheduled)
                {
                    Repository.AddDueTransaction(tx);
                }
                else
                {
                    var total = tx.Value + tx.SpentFee;
                    transfer.To(tx.From, total, out _);
                }

                break;
            case TransactionType.REGISTER_VALIDATOR:
                {
                    if (!transfer.Unlock(tx.From, out _))
                    {
                        throw new Exception("Failed to rollback REGISTER_VALIDATOR");
                    }

                    Events.Add(new ValidatorDisable(tx.From));
                }

                break;
            case TransactionType.DEREGISTER_VALIDATOR:
                {
                    if (!transfer.Lock(tx.From, out _))
                    {
                        throw new Exception("Failed to rollback DEREGISTER_VALIDATOR");
                    }

                    Events.Add(new ValidatorEnable(tx.From));
                }
                break;
        }
    }

    private void RollbackToken(Effect effect, Dictionary<(Address, SHA256Hash), Token> tokens)
    {
        if (effect.TokenId is null)
        {
            return;
        }

        if (!tokens.TryGetToken(effect.Contract, effect.TokenId, Repository, out var token))
        {
            return;
        }

        // revert token consume
        if (effect.ConsumeToken)
        {
            token.IsConsumed = false;
            return;
        }

        // if effect originates from contract it was minted
        if (effect.From == effect.Contract)
        {
            Repository.DeleteToken(token);
            tokens.Remove((token.Contract, token.TokenId));
            return;
        }

        // this is a transfer, update owner
        token.Ledger = effect.From;
    }

    public ITransaction BeginTransaction()
    {
        return Repository.BeginTransaction();
    }
}
