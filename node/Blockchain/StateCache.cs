using System.Diagnostics.CodeAnalysis;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node.Blockchain;

public class StateCache : IStateCache
{
    private Dictionary<SHA256Hash, Block> Blocks = new();
    private Dictionary<SHA256Hash, Vote> Votes = new();
    private Dictionary<SHA256Hash, Transaction> Transactions = new();

    private WalletCache LedgerCache = new();
    private ValidatorCache Validators = new();
    private View CurrentView;
    private ChainState ChainState;

    public StateCache(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStoreRepository>();

        CurrentView = repository.GetLastView() ?? new View();
        ChainState = repository.GetChainState() ?? new ChainState();
    }

    public StateCache()
    {
        CurrentView = new View();
        ChainState =  new ChainState();
    }

    public void Add(Block block)
    {
        Blocks.Add(block.GetHash(), block);
    }

    public void Add(Vote vote)
    {
        Votes.Add(vote.GetHash(), vote);
    }

    public void Add(Transaction tx)
    {
        Transactions.Add(tx.CalculateHash(), tx);
    }

    public void Add(Ledger ledger)
    {
        LedgerCache.Add(ledger.Address, ledger);
    }

    public void Clear()
    {
        Blocks.Clear();
        Votes.Clear();
        Transactions.Clear();
        LedgerCache.Clear();
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
        Transactions.EnsureCapacity(count);
    }

    public ChainState GetCurrentState()
    {
        return ChainState;
    }

    public View GetCurrentView()
    {
        return CurrentView;
    }

    public WalletCache GetLedgers()
    {
        return LedgerCache;
    }

    public ValidatorCache GetValidators()
    {
        return Validators;
    }

    public IEnumerable<SHA256Hash> GetTransactionIds()
    {
        return Transactions.Keys;
    }

    public Dictionary<SHA256Hash, Block> GetBlocks()
    {
        return Blocks;
    }

    public Dictionary<SHA256Hash, Vote> GetVotes()
    {
        return Votes;
    }

    public Dictionary<SHA256Hash, Transaction> GetTransactions()
    {
        return Transactions;
    }

    public int LedgerCount()
    {
        return LedgerCache.Count;
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
        return Transactions.Count;
    }
}
