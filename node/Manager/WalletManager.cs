using Marccacoin.Shared;

namespace Marccacoin;

public class WalletManager : IWalletManager
{
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

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
        using var walletRepository = new WalletRepository(true);

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
        walletRepository.Commit();

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
        wallet.Description = wal.Description;

        walletRepository.Update(wallet);
    }

    public void RollbackWallets(List<Wallet> wallets, long blockId)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository(true);

        walletRepository.RollbackWallets(wallets, blockId);

        walletRepository.Commit();
    }

    public void UpdateWallets(IEnumerable<Wallet> wallets)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository(true);

        walletRepository.UpdateWallets(wallets);

        walletRepository.Commit();
    }

    public Wallet GetNodeWallet()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var walletRepository = new WalletRepository();

        return walletRepository.GetNodeWallet();
    }
}
