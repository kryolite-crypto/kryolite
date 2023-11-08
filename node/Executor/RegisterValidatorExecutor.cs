using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class RegisterValidatorExecutor : IExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public RegisterValidatorExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        if (!Context.TryGetValidator(tx.From!, out var validator))
        {
            validator = new Validator { NodeAddress = tx.From! };
            Context.AddValidator(validator);
        }

        var ledger = Context.GetOrNewWallet(validator.NodeAddress);

        validator.RewardAddress = tx.To;
        validator.Stake = ledger.Pending;

        ledger.Balance = 0;
        ledger.Pending = 0;
        ledger.Locked = true;

        Context.AddEvent(new ValidatorEnable(tx.From!));
        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
