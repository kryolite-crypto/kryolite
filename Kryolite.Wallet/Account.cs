using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Type;
using NBip32Fast;
using NBip32Fast.Ed25519;

namespace Kryolite.Wallet;

public class Account : ISerializable
{
    public uint Id;
    public Address Address;
    public PublicKey PublicKey;
    public string? Description;

    public Account()
    {
        Address = new();
        PublicKey = new();
    }

    public Account(HdKey master, uint id)
    {
        var key = Ed25519HdKey.Instance.Derive(master, new KeyPathElement(id, true));
        var pubKey = Ed25519HdKey.Instance.GetPublic(key.PrivateKey);

        Id = id;
        PublicKey = pubKey;
        Address = PublicKey.ToAddress();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.ACCOUNT;
    }

    public int GetLength() =>
        Serializer.SizeOf(Id) +
        Serializer.SizeOf(Address) +
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(Description);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Id);
        serializer.Write(Address);
        serializer.Write(PublicKey);
        serializer.Write(Description);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Id);
        serializer.Read(ref Address);
        serializer.Read(ref PublicKey);
        serializer.ReadN(ref Description);
    }
}
