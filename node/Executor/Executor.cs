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
    private IExecutor ValidatorRegExecutor { get; }
    private IExecutor ContractInstallerExecutor { get; }
    private IExecutorContext Context { get; }
    private ILogger Logger { get; set; }

    public Executor(IExecutor contractExecutor, IExecutor transactionExecutor, IExecutor voteExecutor, IExecutor validatorRegExecutor, IExecutor contractInstallerExecutor, IExecutorContext context, ILogger logger)
    {
        ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        TransactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
        VoteExecutor = voteExecutor ?? throw new ArgumentNullException(nameof(voteExecutor));
        ValidatorRegExecutor = validatorRegExecutor ?? throw new ArgumentNullException(nameof(validatorRegExecutor));
        ContractInstallerExecutor = contractInstallerExecutor ?? throw new ArgumentNullException(nameof(contractInstallerExecutor));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Execute(IEnumerable<Transaction> transactions)
    {
        if (transactions.Count() == 0)
        {
            return;
        }

        Context.SetRand(Context.GetTotalStake());

        foreach (var tx in transactions)
        {
            if (tx.ExecutionResult != ExecutionResult.VERIFIED)
            {
                throw new Exception($"expected verified transaction but got {tx.ExecutionResult} ({tx.TransactionType})");
            }

            tx.Height = Context.GetHeight();

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
                case TransactionType.REG_VALIDATOR:
                    tx.ExecutionResult = ValidatorRegExecutor.Execute(tx);
                    break;
                case TransactionType.CONTRACT:
                    tx.ExecutionResult = ContractInstallerExecutor.Execute(tx);
                    break;
            }
        }

        Context.Save();
    }
}
