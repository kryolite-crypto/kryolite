using Kryolite.Shared;

namespace Kryolite.Node;

public interface IWalletManager
{
    List<WalletTransaction> GetTransactions(int count);
    Wallet CreateWallet(WalletType walletType = WalletType.WALLET);
    Wallet? GetWallet(string address);
    Dictionary<string, Wallet> GetWallets();
    void UpdateWallet(Wallet wallet);
    void UpdateWallets(IEnumerable<Wallet> wallets);
    void RollbackWallets(List<Wallet> wallets, long blockId);
    Wallet? GetNodeWallet();
}
