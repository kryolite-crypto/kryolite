using System.Diagnostics.CodeAnalysis;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Blockchain;

public interface IStateCache
{
    void Add(Transaction tx);
    bool Contains(SHA256Hash hash);
    bool Remove(SHA256Hash id, [MaybeNullWhen(false)] out Transaction tx);
    int TransactionCount();
    IEnumerable<SHA256Hash> GetTransactionIds();
    Dictionary<SHA256Hash, Transaction> GetTransactions();
    void ClearTransactions();
    void EnsureTransactionCapacity(int count);

    void Add(Ledger ledger);
    bool Contains(Address address);
    bool TryGet(Address address, [MaybeNullWhen(false)] out Ledger ledger);
    int LedgerCount();
    Dictionary<Address, Ledger> GetLedgers();
    void ClearLedgers();
    void EnsureLedgerCapacity(int count);

    void SetChainState(ChainState chainState);
    void SetView(View view);

    ChainState GetCurrentState();
    View GetCurrentView();
}
