using Kryolite.Shared;

namespace Kryolite.Node.Repository;

public interface IWalletRepository
{
    bool WalletExists();
    void CreateFromSeed(ReadOnlySpan<byte> seed);
    Wallet.Account CreateAccount();
    Wallet.Account? GetAccount(Address address);
    PrivateKey? GetPrivateKey(PublicKey publicKey);
    void UpdateDescription(Address address, string description);
    Dictionary<Address, Wallet.Account> GetAccounts();
}
