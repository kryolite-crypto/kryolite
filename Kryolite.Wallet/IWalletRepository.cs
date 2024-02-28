using Kryolite.Shared;

namespace Kryolite.Wallet;

public interface IWalletRepository
{
    bool WalletExists();
    void CreateFromSeed(ReadOnlySpan<byte> seed);
    Account CreateAccount();
    Account? GetAccount(Address address);
    PrivateKey? GetPrivateKey(PublicKey publicKey);
    void UpdateDescription(Address address, string description);
    Dictionary<Address, Account> GetAccounts();
}
