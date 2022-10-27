using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;

namespace Marccacoin;

public interface IMempoolManager
{
    void AddTransaction(Transaction transaction);
    void AddTransactions(List<Transaction> transaction, bool broadcast);
    List<Transaction> GetTransactions();
    void RemoveTransactions(IEnumerable<Transaction> transactions);
    bool HasTransaction(Transaction transaction);
    ulong GetPending(Address address);
    IDisposable OnTransactionAdded(ITargetBlock<Transaction> action);
    IDisposable OnTransactionRemoved(ITargetBlock<Transaction> action);
}
