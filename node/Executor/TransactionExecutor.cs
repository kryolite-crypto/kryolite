using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class TransactionExecutor : ITransactionExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public TransactionExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        var wallet = Context.GetOrNewWallet(tx.To);

        checked
        {
            wallet.Pending -= tx.Value;
            wallet.Balance += tx.Value;
        }

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        var from = Context.GetWallet(tx.From);
        var to = Context.GetWallet(tx.To);

        checked
        {
            if (tx.TransactionType == TransactionType.PAYMENT && from is not null)
            {
                from.Balance += tx.Value;
            }

            if (to is not null)
            {
                to.Balance -= tx.Value;
            }
        }
    }
}
