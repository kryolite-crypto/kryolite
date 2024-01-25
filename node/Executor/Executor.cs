using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class Executor
{
    private ContractExecutor ContractExecutor { get; }
    private TransactionExecutor TransactionExecutor { get; }
    private RegisterValidatorExecutor ValidatorRegExecutor { get; }
    private DeregisterValidatorExecutor ValidatorDeregExecutor { get; }
    private ContractInstallerExecutor ContractInstallerExecutor { get; }
    private IExecutorContext Context { get; }
    private ILogger Logger { get; set; }

    public Executor(IExecutorContext context, ILogger logger)
    {
        ContractExecutor = new(context, logger);
        TransactionExecutor = new(context, logger);
        ValidatorRegExecutor = new(context, logger);
        ValidatorDeregExecutor = new(context, logger);
        ContractInstallerExecutor = new(context, logger);
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Execute(List<Transaction> transactions, View view)
    {
        // Collect due transactions
        var dueTransactions = Context.GetRepository().GetDueTransactions(view.Timestamp, true);
        var dueHashes = dueTransactions.Select(x => x.CalculateHash()).ToList();

        if (dueTransactions.Count != 0)
        {
            Logger.LogDebug("Found {count} due transactions to execute", dueTransactions.Count);
        }

        view.ScheduledTransactions.AddRange(dueHashes);
        transactions.AddRange(dueTransactions);

        if (transactions.Count == 0)
        {
            return;
        }

        var seed = view.Timestamp;

        foreach (var tx in transactions)
        {
            seed += tx.Timestamp;
        }

        Context.SetRand(seed);

        foreach (var tx in transactions)
        {
            switch (tx.TransactionType)
            {
                case TransactionType.DEV_REWARD:
                case TransactionType.STAKE_REWARD:
                case TransactionType.BLOCK_REWARD:
                        var wallet = Context.GetOrNewWallet(tx.To);
                        wallet.Pending += tx.Value;

                        view.Rewards.Add(tx.CalculateHash());

                        goto case TransactionType.PAYMENT;
                case TransactionType.PAYMENT:
                    if (tx.Timestamp > view.Timestamp)
                    {
                        Logger.LogDebug("Added scheduled transaction ({hash}), to execute at {timestamp}", tx.CalculateHash(), DateTimeOffset.FromUnixTimeMilliseconds(tx.Timestamp));
                        tx.ExecutionResult = ExecutionResult.SCHEDULED;
                        continue;
                    }

                    tx.ExecutionResult = TransactionExecutor.Execute(tx);

                    if (tx.To.IsContract())
                    {
                        tx.ExecutionResult = ContractExecutor.Execute(tx, view);
                    }

                    if (tx.ExecutionResult != ExecutionResult.SUCCESS)
                    {
                        // should only end up here if Contract execution succeeds but refuses to complete transaction
                        TransactionExecutor.Rollback(tx);
                    }

                    break;
                case TransactionType.REGISTER_VALIDATOR:
                    tx.ExecutionResult = ValidatorRegExecutor.Execute(tx);
                    break;
                case TransactionType.DEREGISTER_VALIDATOR:
                    tx.ExecutionResult = ValidatorDeregExecutor.Execute(tx);
                    break;
                case TransactionType.CONTRACT:
                    if (tx.Timestamp > view.Timestamp)
                    {
                        Logger.LogDebug("Added scheduled transaction ({hash}), to execute at {timestamp}", tx.CalculateHash(), DateTimeOffset.FromUnixTimeMilliseconds(tx.Timestamp));
                        tx.ExecutionResult = ExecutionResult.SCHEDULED;
                        continue;
                    }

                    tx.ExecutionResult = ContractInstallerExecutor.Execute(tx, view);
                    break;
            }
        }

        Context.Save();
    }
}
