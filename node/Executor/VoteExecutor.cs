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
        if (Constant.SEED_VALIDATORS.Contains(tx.PublicKey!))
        {
            return ExecutionResult.SUCCESS;
        }

        var reward = Constant.VALIDATOR_REWARD * (tx.Value / Context.GetTotalStake());

        var wallet = Context.GetOrNewWallet(tx.To);

        checked
        {
            wallet.Balance += reward;
        }

        if (Context.GetRepository().IsValidator(tx.To!))
        {
            Context.GetRepository().SetStake(tx.To!, wallet.Balance);
        }

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
