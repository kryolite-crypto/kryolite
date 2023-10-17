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
    private IExecutor ValidatorRegExecutor { get; }
    private IExecutor ContractInstallerExecutor { get; }
    private IExecutorContext Context { get; }
    private ILogger Logger { get; set; }

    public Executor(IExecutor contractExecutor, IExecutor transactionExecutor, IExecutor validatorRegExecutor, IExecutor contractInstallerExecutor, IExecutorContext context, ILogger logger)
    {
        ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        TransactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
        ValidatorRegExecutor = validatorRegExecutor ?? throw new ArgumentNullException(nameof(validatorRegExecutor));
        ContractInstallerExecutor = contractInstallerExecutor ?? throw new ArgumentNullException(nameof(contractInstallerExecutor));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Execute(IEnumerable<Transaction> transactions, View view)
    {
        if (transactions.Count() == 0)
        {
            return;
        }

        Context.SetRand((long)Context.GetTotalStake());

        foreach (var tx in transactions)
        {
            switch (tx.TransactionType)
            {
                case TransactionType.DEV_REWARD:
                case TransactionType.STAKE_REWARD:
                case TransactionType.BLOCK_REWARD:
                case TransactionType.PAYMENT:
                    if (tx.TransactionType == TransactionType.STAKE_REWARD)
                    {
                        // tx.Value contains full stake at this point, update to actual reward
                        tx.Value = (ulong)Math.Floor(Constant.VALIDATOR_REWARD * (tx.Value / (double)Context.GetTotalStake()));

                        // Update pending since it will be subtracted later on
                        var wallet = Context.GetOrNewWallet(tx.To);
                        wallet.Pending += tx.Value;

                        view.Rewards.Add(tx.CalculateHash());
                    }
                    else if (tx.TransactionType == TransactionType.DEV_REWARD)
                    {
                        // Update pending since it will be subtracted later on
                        var wallet = Context.GetOrNewWallet(tx.To);
                        wallet.Pending += tx.Value;

                        view.Rewards.Add(tx.CalculateHash());
                    }
                    else if (tx.TransactionType == TransactionType.BLOCK_REWARD)
                    {
                        view.Rewards.Add(tx.CalculateHash());
                    }

                    tx.ExecutionResult = TransactionExecutor.Execute(tx);

                    if (tx.To.IsContract())
                    {
                        tx.ExecutionResult = ContractExecutor.Execute(tx);
                    }

                    if (tx.ExecutionResult != ExecutionResult.SUCCESS)
                    {
                        // should only end up here if Contract execution succeeds but refuses to complete transaction
                        TransactionExecutor.Rollback(tx);
                    }

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
