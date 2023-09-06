using Kryolite.Shared;

namespace Kryolite.Node;

public interface IWalletManager
{
    Wallet CreateWallet();
    Wallet? GetWallet(Address address);
    Dictionary<Address, Wallet> GetWallets();
    void UpdateDescription(Address address, string description);
}
