using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Executor;

public interface IViewExecutor
{
    ExecutionResult Execute(Transaction tx);
}
