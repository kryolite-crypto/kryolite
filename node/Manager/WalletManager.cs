using Kryolite.Node.Repository;
using Kryolite.Shared;

namespace Kryolite.Node;

public class WalletManager : IWalletManager
{
    private static readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    public IWalletRepository Repository { get; }

    public WalletManager(IWalletRepository repository)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Wallet? GetWallet(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.Get(address);
    }

    public Dictionary<Address, Wallet> GetWallets()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetWallets();
    }

    public Wallet CreateWallet()
    {
        using var _ = rwlock.EnterWriteLockEx();

        var wallet = Wallet.Create();

        Repository.Add(wallet);
        return wallet;
    }

    public void UpdateDescription(Address address, string description)
    {
        using var _ = rwlock.EnterWriteLockEx();
        Repository.UpdateDescription(address, description);
    }
}
