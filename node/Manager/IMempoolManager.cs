using Marccacoin.Shared;

namespace Marccacoin;

public interface IMempoolManager
{
    void AddTransaction(Transaction transaction);
    List<Transaction> GetTransactions();
    void RemoveTransactions(List<Transaction> transactions);
}
