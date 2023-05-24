using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class Executor
{
    private IContractExecutor ContractExecutor { get; }
    private ITransactionExecutor TransactionExecutor { get; }
    private IViewExecutor ViewExecutor { get; }
    private IExecutorContext Context { get; }
    private ILogger Logger { get; set; }

    public Executor(IContractExecutor contractExecutor, ITransactionExecutor transactionExecutor, IViewExecutor viewExecutor, IExecutorContext context, ILogger logger)
    {
        ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        TransactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
        ViewExecutor = viewExecutor ?? throw new ArgumentNullException(nameof(viewExecutor));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Execute(List<Transaction> transactions)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        Context.SetRand((long)transactions.Average(x => x.Timestamp));

        foreach (var tx in transactions.OrderBy(x => x.TransactionId))
        {
            switch (tx.TransactionType)
            {
                case TransactionType.PAYMENT:
                case TransactionType.BLOCK:
                    if (tx.To is null)
                    {
                        // skip
                        continue;
                    }

                    tx.ExecutionResult = TransactionExecutor.Execute(tx);

                    if (tx.To.IsContract())
                    {
                        tx.ExecutionResult = ContractExecutor.Execute(tx);
                    }

                    if (tx.ExecutionResult != ExecutionResult.SUCCESS)
                    {
                        // should only end up here if Contract execution fails
                        TransactionExecutor.Rollback(tx);
                    }

                    break;
                case TransactionType.VIEW:
                    ViewExecutor.Execute(tx);
                    break;
            }
        }

        Context.Save();
    }
}
