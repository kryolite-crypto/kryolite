using Microsoft.Extensions.Logging;

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
        var validatorRegExecutor = new RegisterValidatorExecutor(context, Logger);
        var validatorDeregExecutor = new DeregisterValidatorExecutor(context, Logger);
        var contractInstallerExecutor = new ContractInstallerExecutor(context, Logger);

        return new Executor(contractExecutor, transactionExecutor, validatorRegExecutor, validatorDeregExecutor, contractInstallerExecutor, context, Logger);
    }
}
