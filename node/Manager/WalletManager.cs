using Kryolite.Shared;

namespace Kryolite.Node;

public class WalletManager : IWalletManager
{
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    public WalletManager()
    {

    }

    public Dictionary<string, Wallet> GetWallets()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var walletRepository = new WalletRepository();

        return walletRepository.GetWallets();
    }

    public List<WalletTransaction> GetTransactions(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var walletRepository = new WalletRepository();

        return walletRepository.GetLastTransactions(count);
    }

    public Wallet CreateWallet(WalletType walletType = WalletType.WALLET)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository();

        if (walletType == WalletType.NODE) {
            var nodeWallet = walletRepository.GetNodeWallet();

            if (nodeWallet != null) {
                return nodeWallet;
            }
        }

        var wallet = new Wallet
        {
            Type = walletType
        };

        walletRepository.Add(wallet);
        return wallet;
    }

    /**
        Only allowed to update Wallet description
    **/
    public void UpdateWallet(Wallet wal)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository();

        var wallet = walletRepository.Get(wal.Address);

        if (wallet is null)
        {
            return;
        }

        wallet.Description = wal.Description;
        walletRepository.Update(wallet);
    }

    public void RollbackWallets(List<Wallet> wallets, long blockId)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository();

        walletRepository.RollbackWallets(wallets, blockId);
    }

    public void UpdateWallets(IEnumerable<Wallet> wallets)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository();

        walletRepository.UpdateWallets(wallets);
    }

    public Wallet? GetNodeWallet()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var walletRepository = new WalletRepository();

        return walletRepository.GetNodeWallet();
    }
}
