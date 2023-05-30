using Kryolite.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kryolite.Node;

public class WalletRepository : IDisposable
{
    public WalletContext Context { get; }

    private static PooledDbContextFactory<WalletContext>? Factory { get; set; }

    public WalletRepository()
    {
        if (Factory is null)
        {
            var walletPath = Path.Join(BlockchainService.DATA_PATH, "wallet.dat");

            var options = new DbContextOptionsBuilder<WalletContext>()
                .UseSqlite($"Data Source={walletPath}")
                .EnableThreadSafetyChecks(false)
                //.EnableSensitiveDataLogging()
                //.LogTo(Console.WriteLine)
                .Options;

            Factory = new PooledDbContextFactory<WalletContext>(options);

            var ctx = Factory.CreateDbContext();

            //Context.Database.Migrate();
            ctx.Database.EnsureCreated();

            FormattableString cmd = $@"
            pragma threads = 4;
            pragma journal_mode = wal; 
            pragma synchronous = normal;
            pragma temp_store = default; 
            pragma mmap_size = -1;";

            ctx.Database.ExecuteSql(cmd);
        }

        Context = Factory.CreateDbContext();
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

    public Wallet? GetWallet(string address)
    {
        return Context.Wallets
            .Where(x => x.Address == address)
            .FirstOrDefault();
    }

    public Dictionary<string, Wallet> GetWallets()
    {
        return Context.Wallets
            .ToDictionary(x => x.Address, x => x);
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
            .Where(x => x.WalletType == WalletType.VALIDATOR)
            .SingleOrDefault();
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}
