using Kryolite.Node.Blockchain;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class DeregisterValidatorExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public DeregisterValidatorExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        if (Context.Transfer.Unlock(tx.From, out var executionResult))
        {
            Context.AddEvent(new ValidatorDisable(tx.From));
        }

        return executionResult;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
