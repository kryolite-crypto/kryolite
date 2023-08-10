using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;
using QuikGraph;
using System.Security.Cryptography;

namespace Kryolite.Node.Executor;

public class Executor
{
    private IExecutor ContractExecutor { get; }
    private IExecutor TransactionExecutor { get; }
    private IExecutor VoteExecutor { get; }
    private IExecutorContext Context { get; }
    private ILogger Logger { get; set; }

    public Executor(IExecutor contractExecutor, IExecutor transactionExecutor, IExecutor voteExecutor, IExecutorContext context, ILogger logger)
    {
        ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        TransactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
        VoteExecutor = voteExecutor ?? throw new ArgumentNullException(nameof(voteExecutor));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Execute(IEnumerable<Transaction> transactions, long height)
    {
        if (transactions.Count() == 0)
        {
            return;
        }

        Context.SetRand((long)transactions.Average(x => x.Timestamp));

        foreach (var tx in transactions)
        {
            tx.Height = height;

            switch (tx.TransactionType)
            {
                case TransactionType.BLOCK:
                case TransactionType.PAYMENT:
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
                case TransactionType.VOTE:
                    tx.ExecutionResult = VoteExecutor.Execute(tx);
                    break;
            }
        }

        Context.Save();
    }
}
