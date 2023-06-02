using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class ViewExecutor : IViewExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public ViewExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        if (tx is not View view)
        {
            return ExecutionResult.INVALID_TRANSACTION_TYPE;
        }

        /*var votes = view.Votes
            .Where(x => !Constant.SEED_VALIDATORS.Contains(x.PublicKey))
            .ToList();

        if (votes.Count == 0)
        {
            return ExecutionResult.NO_VOTES;
        }

        var reward = Constant.VALIDATOR_REWARD / votes.Count;

        foreach (var vote in votes)
        {
            var wallet = Context.GetOrNewWallet(vote.PublicKey.ToAddress());

            checked
            {
                wallet.Balance += (ulong)reward;
            }
        }*/

        return ExecutionResult.SUCCESS;
    }
}
