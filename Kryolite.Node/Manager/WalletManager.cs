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

    public Wallet.Account? GetAccount(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetAccount(address);
    }

    public Dictionary<Address, Wallet.Account> GetAccounts()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetAccounts();
    }

    public Wallet.Account CreateAccount()
    {
        using var _ = rwlock.EnterWriteLockEx();
        return Repository.CreateAccount();
    }

    public void UpdateDescription(Address address, string description)
    {
        using var _ = rwlock.EnterWriteLockEx();
        Repository.UpdateDescription(address, description);
    }

    public PrivateKey GetPrivateKey(PublicKey publicKey)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetPrivateKey(publicKey)!;
    }

    public bool WalletExists()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.WalletExists();
    }

    public void CreateWalletFromSeed(ReadOnlySpan<byte> seed)
    {
        using var _ = rwlock.EnterWriteLockEx();
        Repository.CreateFromSeed(seed);
    }
}
