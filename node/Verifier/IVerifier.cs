using Kryolite.Shared.Blockchain;

namespace Kryolite.Node;

public interface IVerifier
{
    void Verify(ICollection<Transaction> transactions);
    bool Verify(Transaction tx);
}