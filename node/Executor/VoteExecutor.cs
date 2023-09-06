using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class VoteExecutor : IExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public VoteExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        // no reward for seed nodes, but we still allow these transactions to count in weight calculation
        if (Constant.SEED_VALIDATORS.Contains(tx.From!))
        {
            return ExecutionResult.SUCCESS;
        }

        var stake = Context.GetRepository().GetStake(tx.From!) ?? throw new Exception($"stake not found: {tx.From}");

        var reward = Constant.VALIDATOR_REWARD * (stake.Amount / Context.GetTotalStake());
        var wallet = Context.GetOrNewWallet(stake.RewardAddress);

        checked
        {
            wallet.Balance += reward;
        }

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
