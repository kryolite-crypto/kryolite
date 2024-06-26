
using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Type;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace Kryolite.Wallet;

public class WalletRepository : IWalletRepository
{
    private string DataDir { get; }
    private string StorePath { get; }

    public WalletRepository(IConfiguration configuration)
    {
        DataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        StorePath = Path.Combine(DataDir, "wallet.blob");
    }

    public bool WalletExists()
    {
        return File.Exists(StorePath);
    }

    public void CreateFromSeed(ReadOnlySpan<byte> seed)
    {
        if (File.Exists(StorePath))
        {
            throw new Exception("wallet already exists");
        }

        Commit(Wallet.CreateFromSeed(seed));
    }

    public Account CreateAccount()
    {
        using var mutex = new Mutex(false, StorePath.Replace(Path.DirectorySeparatorChar.ToString(), ""));

        var hasHandle = false;

        try
        {
            hasHandle = mutex.WaitOne( Timeout.Infinite, false );

            var store = Load();
            var account = store.CreateAccount();

            Commit(store);

            return account;
        }
        finally
        {
            if ( hasHandle )
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public Account? GetAccount(Address address)
    {
        var store = Load();
        return store.GetAccount(address);
    }

    public PrivateKey? GetPrivateKey(PublicKey publicKey)
    {
        var store = Load();
        return store.GetPrivateKey(publicKey);
    }

    public void UpdateDescription(Address address, string description)
    {
        using var mutex = new Mutex(false, StorePath.Replace(Path.DirectorySeparatorChar.ToString(), ""));

        var hasHandle = false;

        try
        {
            hasHandle = mutex.WaitOne( Timeout.Infinite, false );

            var store = Load();
            var wallet = store.GetAccount(address);

            if (wallet is not null)
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

    public Dictionary<Address, Account> GetAccounts()
    {
        if (!File.Exists(StorePath))
        {
            return new();
        }

        return Load().Accounts.ToDictionary(x => x.Address, y => y);
    }

    public void Backup()
    {
        if (!File.Exists(StorePath))
        {
            return;
        }

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

    private Wallet Load()
    {
        var bytes = File.ReadAllBytes(StorePath);
        return Serializer.Deserialize<Wallet>(bytes) ?? throw new Exception("failed to deserialize wallet");
    }

    private void Commit(Wallet container)
    {
        var bytes = Serializer.Serialize<Wallet>(container);
        File.WriteAllBytes(StorePath, bytes);
    }
}
