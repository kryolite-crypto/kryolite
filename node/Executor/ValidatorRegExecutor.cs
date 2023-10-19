using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
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
        var stake = Context.GetRepository().GetStake(tx.From!) ?? new Validator { NodeAddress = tx.From! };
        var ledger = Context.GetOrNewWallet(stake.NodeAddress);

        ledger.Balance = checked (ledger.Balance + stake.Stake - tx.Value);

        stake.Stake = tx.Value;
        stake.RewardAddress = tx.To;

        Context.GetRepository().SetStake(tx.From!, stake, Context.GetHeight());
        Context.AddEvent(stake.Stake >= Constant.MIN_STAKE ? 
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
