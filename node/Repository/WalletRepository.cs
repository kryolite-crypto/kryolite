using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class WalletRepository : TransactionalRepository
{
    public WalletRepository(bool transactional = false) : base("data/wallet.dat", transactional)
    {
        BsonMapper.Global.Entity<Wallet>()
            .DbRef(x => x.WalletTransactions, typeof(WalletTransaction).Name);

        Database.GetCollection<Wallet>()
            .EnsureIndex(x => x.Address, true);
    }

    public void Add(Wallet wallet)
    {
        Database.GetCollection<WalletTransaction>().Insert(wallet.WalletTransactions);
        Database.GetCollection<Wallet>().Insert(wallet);
    }

    public Wallet Get(Address address)
    {
        return Database.GetCollection<Wallet>()
            .IncludeCollection(x => x.WalletTransactions)
            .Query()
            .Where(x => x.Address == address.ToString())
            .FirstOrDefault();
    }

    public void Update(Wallet wallet)
    {
        Database.GetCollection<WalletTransaction>()
            .Upsert(wallet.WalletTransactions);
        Database.GetCollection<Wallet>()
            .Update(wallet);
    }

    public void UpdateWallets(IEnumerable<Wallet> wallets)
    {
        foreach (var wallet in wallets) {
            Database.GetCollection<WalletTransaction>()
                .Upsert(wallet.WalletTransactions);
        }

        Database.GetCollection<Wallet>()
            .Update(wallets);
    }

    public Dictionary<string, Wallet> GetWallets()
    {
        return Database.GetCollection<Wallet>()
            .IncludeCollection(x => x.WalletTransactions)
            .FindAll()
            .ToDictionary(x => x.Address, x => x);
    }

    public List<WalletTransaction> GetLastTransactions(int count)
    {
        return Database.GetCollection<WalletTransaction>()
            .Query()
            .OrderByDescending(x => x.Timestamp)
            .Limit(count)
            .ToList();
    }
}
