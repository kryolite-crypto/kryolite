using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class DeregisterValidatorExecutor : IExecutor
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
        if (!Context.TryGetValidator(tx.From!, out var validator))
        {
            return ExecutionResult.UNKNOWN;
        }

        var ledger = Context.GetOrNewWallet(validator.NodeAddress);

        validator.Stake = 0;

        ledger.Balance = validator.Stake;
        ledger.Pending = checked(ledger.Pending - validator.Stake);
        ledger.Locked = false;

        Context.AddEvent(new ValidatorDisable(tx.From!));
        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
