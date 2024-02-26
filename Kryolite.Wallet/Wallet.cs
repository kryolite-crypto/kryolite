using Kryolite.Shared;
using MemoryPack;
using NBip32Fast;

namespace Kryolite.Wallet;

[MemoryPackable]
public partial class Wallet
{
    public PrivateKey PrivateKey { get; set; }
    public uint ChainCode { get; set; }
    public List<Account> Accounts { get; set; } = [];

    [MemoryPackConstructor]
    public Wallet(PrivateKey privateKey, uint chainCode, List<Account> accounts)
    {
        PrivateKey = privateKey;
        ChainCode = chainCode;
        Accounts = accounts;
    }

    public Wallet(HdKey hdKey)
    {
        PrivateKey = hdKey.PrivateKey.ToArray();
        ChainCode = 0;
    }

    public static Wallet CreateFromSeed(ReadOnlySpan<byte> seed)
    {
        return new Wallet(Derivation.Ed25519.GetMasterKeyFromSeed(seed));
    }

    public static Wallet CreateFromRandomSeed()
    {
        var seed = new byte[32];
        Random.Shared.NextBytes(seed);
        return new Wallet(Derivation.Ed25519.GetMasterKeyFromSeed(seed));
    }

    public Account CreateAccount()
    {
        var hdKey = new HdKey(PrivateKey, KeyPathElement.SerializeUInt32(ChainCode).Span);
        var account = new Account(hdKey, ChainCode++);

        Accounts.Add(account);

        return account;
    }

    public Account? GetAccount(PublicKey publicKey)
    {
        return Accounts.Where(x => x.PublicKey == publicKey).FirstOrDefault();
    }

    public Account? GetAccount(Address address)
    {
        return Accounts.Where(x => x.Address == address).FirstOrDefault();
    }

    public PrivateKey? GetPrivateKey(PublicKey publicKey)
    {
        var account = GetAccount(publicKey);

        if (account is null)
        {
            return null;
        }

        var master = new HdKey(PrivateKey, KeyPathElement.SerializeUInt32(account.Id).Span);
        return Derivation.Ed25519.Derive(master, new KeyPathElement(account.Id, true)).PrivateKey.ToArray();
    }

    public PrivateKey? GetPrivateKey(Address address)
    {
        var account = GetAccount(address);

        if (account is null)
        {
            return null;
        }

        var master = new HdKey(PrivateKey, KeyPathElement.SerializeUInt32(account.Id).Span);
        return Derivation.Ed25519.Derive(master, new KeyPathElement(account.Id, true)).PrivateKey.ToArray();
    }
}
