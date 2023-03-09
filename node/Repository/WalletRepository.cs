using Kryolite.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kryolite.Node;

public class WalletRepository : IDisposable
{
    public WalletContext Context { get; }

    public WalletRepository()
    {
        var walletPath = Path.Join(BlockchainService.DATA_PATH, "wallet.dat");

        var options = new DbContextOptionsBuilder<WalletContext>()
            .UseSqlite($"Data Source={walletPath}")
            .Options;

        Context = new WalletContext(options);
        Context.Database.Migrate();
    }

    public void Add(Wallet wallet)
    {
        Context.Wallets.Add(wallet);
        Context.SaveChanges();
    }

    public Wallet? Get(Address address)
    {
        return Context.Wallets
            .Where(x => x.Address == address.ToString())
            .FirstOrDefault();
    }

    public void Update(Wallet wallet)
    {
        Context.Wallets.Update(wallet);
        Context.SaveChanges();
    }

    public void UpdateWallets(IEnumerable<Wallet> wallets)
    {
        Context.Wallets.UpdateRange(wallets);
        Context.SaveChanges();
    }

    public void RollbackWallets(IEnumerable<Wallet> wallets, long height)
    {
        var toRemove = Context.Transactions.Where(x => x.Height >= height)
            .ToList();

        Context.Transactions.RemoveRange(toRemove);
        Context.SaveChanges();
    }

    public Dictionary<string, Wallet> GetWallets()
    {
        return Context.Wallets.ToDictionary(x => x.Address, x => x);
    }

    public List<WalletTransaction> GetLastTransactions(int count)
    {
        return Context.Transactions
            .OrderByDescending(x => x.Timestamp)
            .Take(count)
            .ToList();
    }

    public Wallet? GetNodeWallet()
    {
        return Context.Wallets
            .Where(x => x.Type == WalletType.NODE)
            .SingleOrDefault();
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}
