using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Executor;

public interface IExecutor
{
    ExecutionResult Execute(Transaction tx);
    void Rollback(Transaction tx);
}
