using System.Security.Cryptography;
using Kryolite.Node.Blockchain;
using Kryolite.Redbus;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;
using Redbus.Events;

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
        var from = Context.GetOrNewWallet(tx.From);
        var stake = Context.GetRepository().GetStake(tx.From!) ?? new Stake();

        from.Balance = checked(from.Balance + stake.Amount);
        stake.PushStake(tx.Value, tx.To!);
        from.Balance = checked (from.Balance - stake.Amount);

        Context.GetRepository().SetStake(tx.From!, stake);
        Context.GetEventBus().Publish<EventBase>(stake.Amount >= Constant.MIN_STAKE ? 
            new ValidatorEnable(tx.From!) : 
            new ValidatorDisable(tx.From!)
        );
        
        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
