using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class TransactionExecutor : IExecutor
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
        Context.Transfer.To(tx.To, tx.Value, out var wallet);
        wallet.Pending = checked (wallet.Pending - tx.Value);

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        if (!Context.Transfer.From(tx.To, tx.Value, out _, out _))
        {
            throw new Exception("failed to rollback transactions");
        }

        Context.Transfer.To(tx.From!, tx.Value, out _);
    }
}
