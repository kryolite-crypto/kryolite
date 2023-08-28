using System.Diagnostics.CodeAnalysis;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using QuikGraph;

namespace Kryolite.Node.Blockchain;

public class StateCache : IStateCache
{
    private Dictionary<SHA256Hash, Transaction> PendingCache = new();
    private AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> PendingGraph = new();

    private Dictionary<Address, Ledger> LedgerCache = new();
    private View CurrentView;
    private ChainState ChainState;
    private Wallet Node;

    public StateCache(IStoreRepository store, IWalletManager walletManager)
    {
        CurrentView = store.GetLastView() ?? new View();
        ChainState = store.GetChainState() ?? new ChainState();
        Node = walletManager.GetNodeWallet() ?? Wallet.Create(WalletType.VALIDATOR);
    }

    public void Add(Transaction tx)
    {
        PendingCache.Add(tx.TransactionId, tx);

        PendingGraph.AddVertex(tx.TransactionId);

        foreach (var parent in tx.Parents)
        {
            if (PendingGraph.ContainsVertex(parent))
            {
                PendingGraph.AddEdge(new Edge<SHA256Hash>(tx.TransactionId, parent));
            }
        }
    }

    public void Add(Ledger ledger)
    {
        LedgerCache.Add(ledger.Address, ledger);
    }

    public void ClearLedgers()
    {
        LedgerCache = new();
    }

    public void ClearTransactions()
    {
        PendingCache = new();
        PendingGraph = new();
    }

    public bool Contains(SHA256Hash hash)
    {
        return PendingCache.ContainsKey(hash);
    }

    public bool Contains(Address address)
    {
        return LedgerCache.ContainsKey(address);
    }

    public void EnsureLedgerCapacity(int count)
    {
        LedgerCache.EnsureCapacity(count);
    }

    public void EnsureTransactionCapacity(int count)
    {
        PendingCache.EnsureCapacity(count);
    }

    public ChainState GetCurrentState()
    {
        return ChainState;
    }

    public View GetCurrentView()
    {
        return CurrentView;
    }

    public Dictionary<Address, Ledger> GetLedgers()
    {
        return LedgerCache;
    }

    public AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> GetPendingGraph()
    {
        return PendingGraph;
    }

    public IEnumerable<SHA256Hash> GetTransactionIds()
    {
        return PendingCache.Keys;
    }

    public Dictionary<SHA256Hash, Transaction> GetTransactions()
    {
        return PendingCache;
    }

    public int LedgerCount()
    {
        return LedgerCache.Count;
    }

    public bool Remove(SHA256Hash id, [MaybeNullWhen(false)]  out Transaction tx)
    {
        if (PendingCache.Remove(id, out tx))
        {
            if (!PendingGraph.RemoveVertex(id))
            {
                // Failed to fully remove pending transaction, re-add to cache
                PendingCache.Add(id, tx);
                return false;
            }

            return true;
        }

        return false;
    }

    public void SetChainState(ChainState chainState)
    {
        ChainState = chainState;
    }

    public void SetView(View view)
    {
        CurrentView = view;
    }

    public int TransactionCount()
    {
        return PendingCache.Count;
    }

    public bool TryGet(Address address, [MaybeNullWhen(false)] out Ledger ledger)
    {
        return LedgerCache.TryGetValue(address, out ledger);
    }
}
