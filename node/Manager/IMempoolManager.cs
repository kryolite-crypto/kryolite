using Marccacoin.Shared;

namespace Marccacoin;

public interface IMempoolManager
{
    void AddTransaction(Transaction transaction);
    void AddTransactions(List<Transaction> transaction);
    List<Transaction> GetTransactions();
    void RemoveTransactions(List<Transaction> transactions);
    public bool HasTransaction(Transaction transaction);
    public ulong GetPending(Address address);
}
