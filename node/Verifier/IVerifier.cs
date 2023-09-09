using System.Collections.Concurrent;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using QuikGraph;

namespace Kryolite.Node;

public interface IVerifier
{
    void Verify(ICollection<Transaction> transactionList);
    bool VerifyTypeOnly(Transaction tx, ConcurrentDictionary<SHA256Hash, Transaction> transactionList);
    bool Verify(Transaction tx);
}