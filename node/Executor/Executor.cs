﻿using Kryolite.Shared;
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
    private IExecutor ValidatorDeregExecutor { get; }
    private IExecutor ContractInstallerExecutor { get; }
    private IExecutorContext Context { get; }
    private ILogger Logger { get; set; }

    public Executor(IExecutor contractExecutor, IExecutor transactionExecutor, IExecutor validatorRegExecutor, IExecutor validatorDeregExecutor, IExecutor contractInstallerExecutor, IExecutorContext context, ILogger logger)
    {
        ContractExecutor = contractExecutor ?? throw new ArgumentNullException(nameof(contractExecutor));
        TransactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
        ValidatorRegExecutor = validatorRegExecutor ?? throw new ArgumentNullException(nameof(validatorRegExecutor));
        ValidatorDeregExecutor = validatorDeregExecutor ?? throw new ArgumentNullException(nameof(validatorDeregExecutor));
        ContractInstallerExecutor = contractInstallerExecutor ?? throw new ArgumentNullException(nameof(contractInstallerExecutor));
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
            Logger.LogInformation("Found {count} due transactions to execute", dueTransactions.Count);
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
                        var wallet = Context.GetOrNewWallet(tx.To);
                        wallet.Pending += tx.Value;

                        view.Rewards.Add(tx.CalculateHash());

                        goto case TransactionType.PAYMENT;
                case TransactionType.BLOCK_REWARD:
                        view.Rewards.Add(tx.CalculateHash());

                        goto case TransactionType.PAYMENT;
                case TransactionType.PAYMENT:
                    if (tx.Timestamp > view.Timestamp)
                    {
                        Logger.LogInformation("Added scheduled transaction ({hash}), to execute at {timestamp}", tx.CalculateHash(), DateTimeOffset.FromUnixTimeMilliseconds(tx.Timestamp));
                        tx.ExecutionResult = ExecutionResult.SCHEDULED;
                        continue;
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

                    tx.ExecutionResult = ContractInstallerExecutor.Execute(tx);
                    break;
            }
        }

        Context.Save();
    }
}
