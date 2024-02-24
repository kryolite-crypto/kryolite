using System.Diagnostics.CodeAnalysis;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using QuikGraph;

namespace Kryolite.Node.Blockchain;

public interface IStateCache
{
    void Add(Block block);
    void Add(Vote vote);
    void Add(Transaction tx);
    int TransactionCount();
    IEnumerable<SHA256Hash> GetTransactionIds();
    Dictionary<SHA256Hash, Transaction> GetTransactions();
    Dictionary<SHA256Hash, Block> GetBlocks();
    Dictionary<SHA256Hash, Vote> GetVotes();
    void Clear();
    void EnsureTransactionCapacity(int count);

    void Add(Ledger ledger);
    bool Contains(Address address);
    int LedgerCount();
    WalletCache GetLedgers();
    ValidatorCache GetValidators();
    void EnsureLedgerCapacity(int count);

    void SetChainState(ChainState chainState);
    void SetView(View view);

    ChainState GetCurrentState();
    View GetCurrentView();
}
