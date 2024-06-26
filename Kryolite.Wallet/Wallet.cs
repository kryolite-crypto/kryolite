using Kryolite.ByteSerializer;
using Kryolite.Shared;
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

    public Wallet(HdKey hdKey)
    {
        PrivateKey = hdKey.PrivateKey.ToArray();
        ChainCode = 0;
        Accounts = new();
    }

    public static Wallet CreateFromSeed(ReadOnlySpan<byte> seed)
    {
        return new Wallet(Ed25519HdKey.Instance.GetMasterKeyFromSeed(seed));
    }

    public static Wallet CreateFromRandomSeed()
    {
        var seed = new byte[32];
        Random.Shared.NextBytes(seed);
        return new Wallet(Ed25519HdKey.Instance.GetMasterKeyFromSeed(seed));
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
        return Ed25519HdKey.Instance.Derive(master, new KeyPathElement(account.Id, true)).PrivateKey.ToArray();
    }

    public PrivateKey? GetPrivateKey(Address address)
    {
        var account = GetAccount(address);

        if (account is null)
        {
            return null;
        }

        var master = new HdKey(PrivateKey, KeyPathElement.SerializeUInt32(account.Id).Span);
        return Ed25519HdKey.Instance.Derive(master, new KeyPathElement(account.Id, true)).PrivateKey.ToArray();
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
}
