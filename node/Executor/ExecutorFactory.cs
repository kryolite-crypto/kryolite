﻿using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class ExecutorFactory : IExecutorFactory
{
    private ILogger Logger { get; }

    public ExecutorFactory(ILogger<ExecutorFactory> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Executor Create(IExecutorContext context)
    {
        var contractExecutor = new ContractExecutor(context, Logger);
        var transactionExecutor = new TransactionExecutor(context, Logger);
        var viewExecutor = new ViewExecutor(context, Logger);

        return new Executor(contractExecutor, transactionExecutor, viewExecutor, context, Logger);
    }
}