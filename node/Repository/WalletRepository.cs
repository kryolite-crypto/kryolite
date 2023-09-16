
using Kryolite.Node.Repository;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace Kryolite.Node;

public class WalletRepository : IWalletRepository
{
    private string DataDir { get; }
    private string StorePath { get; }

    public WalletRepository(IConfiguration configuration)
    {
        DataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        StorePath = Path.Combine(DataDir, "wallet.blob");

        if (!File.Exists(StorePath))
        {
            Commit(new WalletContainer());
        }
    }

    public void Add(Wallet wallet)
    {
        using var mutex = new Mutex(false, StorePath.Replace(Path.DirectorySeparatorChar.ToString(), ""));

        var hasHandle = false;

        try
        {
            hasHandle = mutex.WaitOne( Timeout.Infinite, false );

            var store = Load();
            store.Container[wallet.Address] = wallet;
            Commit(store);
        }
        finally
        {
            if ( hasHandle )
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public Wallet? Get(Address address)
    {
        var store = Load();
        
        if (!store.Container.TryGetValue(address, out var wallet))
        {
            return null;
        }

        return wallet;
    }

    public void UpdateDescription(Address address, string description)
    {
        using var mutex = new Mutex(false, StorePath.Replace(Path.DirectorySeparatorChar.ToString(), ""));

        var hasHandle = false;

        try
        {
            hasHandle = mutex.WaitOne( Timeout.Infinite, false );

            var store = Load();
            
            if (store.Container.TryGetValue(address, out var wallet))
            {
                wallet.Description = description;
            }

            Commit(store);
        }
        finally
        {
            if ( hasHandle )
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public Dictionary<Address, Wallet> GetWallets()
    {
        return Load().Container;
    }

    public void Backup()
    {
        var backupFolder = Path.Combine(DataDir, "backup");
        Directory.CreateDirectory(backupFolder);

        var backupPath = Path.Combine(backupFolder, $"wallet-{DateTimeOffset.Now.ToString("yyyyMMddhhmmss")}.blob");
        File.Copy(StorePath, backupPath, true);

        var oldestBackup = Directory.EnumerateFiles(backupFolder)
            .Where(x => x.StartsWith("wallet.blob"))
            .MinBy(x => File.GetCreationTime(x));

        if (oldestBackup is not null && Directory.EnumerateFiles(backupFolder).Count() > 5)
        {
            File.Delete(oldestBackup);
        }
    }

    private WalletContainer Load()
    {
        var bytes = File.ReadAllBytes(StorePath);
        return MessagePackSerializer.Deserialize<WalletContainer>(bytes);
    }

    private void Commit(WalletContainer container)
    {
        var bytes = MessagePackSerializer.Serialize(container);
        File.WriteAllBytes(StorePath, bytes);
    }

    [MessagePackObject]
    public class WalletContainer
    {
        [Key(0)]
        public Dictionary<Address, Wallet> Container = new();
    }
}

