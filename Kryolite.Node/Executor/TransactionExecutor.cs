using Kryolite.Node.Procedure;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class TransactionExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public TransactionExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx, ref Transfer transfer)
    {
        transfer.To(tx.To, tx.Value, out var wallet);
        wallet.Pending = checked(wallet.Pending - tx.Value);

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx, ref Transfer transfer)
    {
        // Refund only the value but no spent gas fees
        if (!transfer.From(tx.To, tx.Value, out _, out _))
        {
            throw new Exception("failed to rollback transactions");
        }

        transfer.To(tx.From!, tx.Value, out _);
    }
}
