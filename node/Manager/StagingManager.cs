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

    public override string CHAIN_NAME => "[STAGING] ";

    private StagingManager(IStoreRepository repository, IKeyRepository keyRepository, IVerifier verifier, IStateCache stateCache, IExecutorFactory executorFactory, ILoggerFactory loggerFactory) : base(repository, keyRepository, verifier, stateCache, executorFactory, loggerFactory.CreateLogger("TransactionManager"))
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        StateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        Logger = loggerFactory.CreateLogger<StagingManager>();
    }

    public static StagingManager Open(string storeName, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var dataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var storePath = Path.Combine(dataDir, $"store.{storeName}");

        var repository = new StoreRepository(storePath);
        var keyRepository = new KeyRepository(configuration);

        var stateCache = new StateCache();
        stateCache.SetChainState(repository.GetChainState() ?? new ChainState());

        var executor = new ExecutorFactory(loggerFactory.CreateLogger<ExecutorFactory>());
        var verifier = new Verifier(repository, stateCache, loggerFactory.CreateLogger<Verifier>());

        return new StagingManager(repository, keyRepository, verifier, stateCache, executor, loggerFactory);
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
        var result = Parallel.ForEach(transactions, (txDto, state) => {
            var tx = new Transaction(txDto);

            if (!Verifier.Verify(tx))
            {
                state.Stop();
            }

            lock(_lock)
            {
                if (!AddTransactionInternal(tx, false))
                {
                    state.Stop();
                }
            }
        });

        return result.IsCompleted;
    }

    public bool LoadBlocks(List<Block> blocks)
    {
        var result = Parallel.ForEach(blocks, (block, state) => {
            if (!Verifier.Verify(block))
            {
                state.Stop();
            }

            lock(_lock)
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
        var result = Parallel.ForEach(votes, (vote, state) => {
            if (!Verifier.Verify(vote))
            {
                state.Stop();
            }

            lock(_lock)
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
            Logger.LogInformation($"Rolling back height {i}");

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
                    RollbackTransaction(i, tx, transfer, ledgers, contracts, tokens, validators);
                }
            }

            var transactions = Repository.GetTransactions(view.Transactions);

            // Rollback in reverse order
            transactions.Reverse();

            foreach (var tx in transactions)
            {
                RollbackTransaction(i, tx, transfer, ledgers, contracts, tokens, validators);
            }

            Repository.DeleteBlocks(view.Blocks);
            Repository.DeleteVotes(view.Votes);
            Repository.Delete(view);
            Repository.DeleteState(i);
        }

        Repository.UpdateWallets(ledgers.Values);
        Repository.UpdateContracts(contracts.Values);
        Repository.UpdateTokens(tokens.Values);

        dbtx.Commit();

        StateCache.SetChainState(Repository.GetChainState() ?? new ChainState());
    }

    private void RollbackTransaction(long height, Transaction tx, Transfer transfer, WalletCache ledgers, Dictionary<Address, Contract> contracts, Dictionary<(Address, SHA256Hash), Token> tokens, ValidatorCache validators)
    {
        if (tx.ExecutionResult != ExecutionResult.SUCCESS)
        {
            // failed contract execution has already refunded this tx
            return;
        }

        var from = tx.From ?? Address.NULL_ADDRESS;
        var to = tx.To ?? Address.NULL_ADDRESS;

        if (!ledgers.TryGetWallet(from, Repository, out var sender))
        {
            sender = new Ledger();
        }

        if (to.IsContract() && contracts.TryGetContract(to, Repository, out var contract))
        {
            // Note: effects need to be rolled back before transaction
            foreach (var effect in tx.Effects)
            {
                transfer.From(effect.To, effect.Value, out _, out _);
                RollbackToken(effect, tokens);
            }

            Repository.DeleteContractSnapshot(to, height);
        }

        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK_REWARD:
            case TransactionType.STAKE_REWARD:
            case TransactionType.DEV_REWARD:
                transfer.From(to, tx.Value, out _, out _);
                break;
            case TransactionType.PAYMENT:
                transfer.From(to, tx.Value, out _, out _);
                transfer.To(from, tx.Value, out _);
                break;
            case TransactionType.CONTRACT:
                transfer.From(to, tx.Value, out _, out _);
                transfer.To(from, tx.Value, out _);

                Repository.DeleteContractSnapshot(to, height);
                Repository.DeleteContractCode(to);
                Repository.DeleteContract(to);

                contracts.Remove(to);
                break;
            case TransactionType.REGISTER_VALIDATOR:
                {
                    if (!validators.TryGetValidator(from, Repository, out var validator))
                    {
                        throw new Exception("missing validator entry");
                    }

                    sender.Balance = validator.Stake;
                    sender.Locked = false;
                    validator.Stake = 0;

                    Events.Add(new ValidatorDisable(from));
                }

                break;
            case TransactionType.DEREGISTER_VALIDATOR:
                {
                    if (!validators.TryGetValidator(from, Repository, out var validator))
                    {
                        throw new Exception("missing validator entry");
                    }

                    validator.Stake = sender.Balance;
                    sender.Balance = 0;
                    sender.Locked = true;

                    Events.Add(new ValidatorEnable(from));
                }
                break;
        }

        Repository.Delete(tx);
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
