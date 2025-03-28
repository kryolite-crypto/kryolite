using Geralt;
using Kryolite.FastSerializer;
using Kryolite.Type;
using NBip32Fast;
using NBip32Fast.Ed25519;

namespace Kryolite.Wallet;

public class Wallet : ISerializable
{
    public PrivateKey PrivateKey;
    public uint ChainCode;
    public List<Account> Accounts;

    public Wallet()
    {
        PrivateKey = new();
        Accounts = new();
    }

    public Wallet(Bip32Key key)
    {
        PrivateKey = new PrivateKey([.. key.Key, .. key.ChainCode]);
        ChainCode = 0;
        Accounts = new();
    }

    public static Wallet CreateFromSeed(ReadOnlySpan<byte> seed)
    {
        var key = new Bip32Key();
        Ed25519HdKey.Instance.GetMasterKeyFromSeed(seed, ref key);
        return new Wallet(key);
    }

    public static Wallet CreateFromRandomSeed()
    {
        var seed = new byte[32];
        Random.Shared.NextBytes(seed);

        var key = new Bip32Key();
        Ed25519HdKey.Instance.GetMasterKeyFromSeed(seed, ref key);

        return new Wallet(key);
    }

    public Account CreateAccount()
    {
        Bip32Key hdKey;

        var masterKey = RegenMasterKey();
        var chainCode = KeyPathElement.Hard(ChainCode);

        Ed25519HdKey.Instance.Derive(ref masterKey, ref chainCode, ref hdKey);
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

        Bip32Key hdKey;

        var masterKey = RegenMasterKey();
        var chainCode = KeyPathElement.Hard(account.Id);

        Ed25519HdKey.Instance.Derive(ref masterKey, ref chainCode, ref hdKey);

        return new PrivateKey([.. hdKey.Key, .. account.PublicKey.Buffer]);
    }

    public PrivateKey? GetPrivateKey(Address address)
    {
        var account = GetAccount(address);

        if (account is null)
        {
            return null;
        }

        Bip32Key hdKey;

        var masterKey = RegenMasterKey();
        var chainCode = KeyPathElement.Hard(account.Id);

        Ed25519HdKey.Instance.Derive(ref masterKey, ref chainCode, ref hdKey);

        return new PrivateKey([.. hdKey.Key, .. account.PublicKey.Buffer]);
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.WALLET;
    }

    public int GetLength() =>
        Serializer.SizeOf(PrivateKey) +
        Serializer.SizeOf(ChainCode) +
        Serializer.SizeOf(Accounts);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PrivateKey);
        serializer.Write(ChainCode);
        serializer.Write(Accounts);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PrivateKey);
        serializer.Read(ref ChainCode);
        serializer.Read(ref Accounts, () => new Account());
    }

    private Bip32Key RegenMasterKey()
    {
        Bip32Key masterKey;

        var chainCode = KeyPathElement.Hard(0u);

        PrivateKey.Buffer.CopyTo(masterKey.Span);
        chainCode.Serialize(masterKey.Span[32..]);

        return masterKey;
    }
}
