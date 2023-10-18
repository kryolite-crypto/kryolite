using System.Runtime.InteropServices;
using System.Xml;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
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

        var ledgers = new Dictionary<Address, Ledger>();
        var contracts = new Dictionary<Address, Contract>();
        var tokens = new Dictionary<(Address, SHA256Hash), Token>();

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
                RollbackTransaction(i, ledgers, contracts, tokens, tx);
            }

            var transactions = Repository.GetTransactions(view.Transactions);

            // Rollback in reverse order
            transactions.Reverse();

            foreach (var tx in transactions)
            {
                RollbackTransaction(i, ledgers, contracts, tokens, tx);
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

    private void RollbackTransaction(long height, Dictionary<Address, Ledger> ledgers, Dictionary<Address, Contract> contracts, Dictionary<(Address, SHA256Hash), Token> tokens, Transaction tx)
    {
        if (tx.ExecutionResult != ExecutionResult.SUCCESS)
        {
            // failed contract execution has already refunded this tx
            return;
        }

        var from = tx.From ?? Address.NULL_ADDRESS;
        var to = tx.To ?? Address.NULL_ADDRESS;

        var sender = ledgers.TryGetWallet(from, Repository) ?? new Ledger();
        var recipient = ledgers.TryGetWallet(to, Repository) ?? new Ledger();
        var contract = to.IsContract() ? contracts.TryGetContract(to, Repository) : null;

        if (contract is not null)
        {
            // Note: effects need to be rolled back before transaction
            foreach (var effect in tx.Effects)
            {
                RollbackEffect(effect, recipient, ledgers, tokens);
            }

            Repository.DeleteContractSnapshot(to, height);
        }

        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK_REWARD:
            case TransactionType.STAKE_REWARD:
            case TransactionType.DEV_REWARD:
                checked
                {
                    recipient.Balance -= tx.Value;
                }
                break;
            case TransactionType.PAYMENT:
                checked
                {
                    sender.Balance += tx.Value;
                    recipient.Balance -= tx.Value;
                }
                break;
            case TransactionType.CONTRACT:
                checked
                {
                    sender.Balance += tx.Value;
                    recipient.Balance -= tx.Value;
                }

                Repository.DeleteContractSnapshot(to, height);
                Repository.DeleteContractCode(to);
                Repository.DeleteContract(to);

                contracts.Remove(to);
                break;
            case TransactionType.REG_VALIDATOR:
                Repository.DeleteStake(from, height);
                var newStake = Repository.GetStakeAtHeight(from, height);

                checked
                {
                    sender.Balance += tx.Value;
                    sender.Balance -= newStake?.Stake ?? 0;
                }
                break;
        }

        Repository.Delete(tx);
    }

    private void RollbackEffect(Effect effect, Ledger recipient, Dictionary<Address, Ledger> ledgers, Dictionary<(Address, SHA256Hash), Token> tokens)
    {
        var eRecipient = ledgers.TryGetWallet(effect.To, Repository);

        recipient.Balance = checked(recipient.Balance + effect.Value);

        if (eRecipient is not null)
        {
            eRecipient.Balance = checked(eRecipient.Balance - effect.Value);
        }

        if (effect.TokenId is null)
        {
            return;
        }

        var token = tokens.TryGetToken(effect.Contract, effect.TokenId, Repository);

        if (token is null)
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
