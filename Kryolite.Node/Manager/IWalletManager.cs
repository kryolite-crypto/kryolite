using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node;

public interface IWalletManager
{
    bool WalletExists();
    void CreateWalletFromSeed(ReadOnlySpan<byte> seed);
    Wallet.Account CreateAccount();
    Wallet.Account? GetAccount(Address address);
    Dictionary<Address, Wallet.Account> GetAccounts();
    void UpdateDescription(Address address, string description);
    PrivateKey GetPrivateKey(PublicKey publicKey);
}
