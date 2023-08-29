using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class ValidatorRegExecutor : IExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public ValidatorRegExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        if (tx.Value >= Constant.MIN_STAKE)
        {
            Context.GetRepository().SetStake(tx.From!, tx.Value);
        }
        else
        {
            Context.GetRepository().DeleteValidator(tx.From!);
        }
        
        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
