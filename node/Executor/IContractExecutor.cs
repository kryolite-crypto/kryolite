using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Executor;

public interface IContractExecutor
{
    ExecutionResult Execute(Transaction tx);
}
