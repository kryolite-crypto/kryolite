using Kryolite.Node.Blockchain;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class RegisterValidatorExecutor
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
        var ledger = Context.GetOrNewWallet(tx.From);
        
        // Return pending balance to wallet to be locked
        ledger.Balance = checked(ledger.Balance + ledger.Pending);
        ledger.Pending = checked(ledger.Pending - ledger.Pending);

        if (Context.Transfer.Lock(tx.From, tx.To, out var executionResult))
        {
            Context.AddEvent(new ValidatorEnable(tx.From));
        }

        return executionResult;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
