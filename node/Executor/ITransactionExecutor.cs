using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Executor;

public interface ITransactionExecutor
{
    ExecutionResult Execute(Transaction tx);
    void Rollback(Transaction tx);
}
