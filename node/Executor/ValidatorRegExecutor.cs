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
        var wallet = Context.GetOrNewWallet(tx.To);
        var stake = Context.GetRepository().GetStake(tx.From!) ?? new Stake();

        if (tx.Value >= Constant.MIN_STAKE)
        {
            stake.Amount = tx.Value;
            stake.RewardAddress = tx.To!;

            Context.GetRepository().SetStake(tx.From!, stake);
        }
        else if (tx.Value == 0)
        {
            Context.GetRepository().DeleteValidator(tx.From!);
        }
        else
        {
            return ExecutionResult.UNKNOWN;
        }
        
        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
