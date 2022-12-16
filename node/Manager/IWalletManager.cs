using Marccacoin.Shared;

namespace Marccacoin;

public interface IWalletManager
{
    List<WalletTransaction> GetTransactions(int count);
    Wallet CreateWallet(WalletType walletType = WalletType.WALLET);
    Dictionary<string, Wallet> GetWallets();
    void UpdateWallet(Wallet wallet);
    void UpdateWallets(IEnumerable<Wallet> wallets);
    void RollbackWallets(List<Wallet> wallets, long blockId);
    Wallet GetNodeWallet();
}
