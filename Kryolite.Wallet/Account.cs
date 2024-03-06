using Kryolite.Shared;
using NBip32Fast;

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
        var key = Derivation.Ed25519.Derive(master, new KeyPathElement(id, true));
        var pubKey = Derivation.Ed25519.GetPublic(key.PrivateKey);

        Id = id;
        PublicKey = pubKey;
        Address = PublicKey.ToAddress();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.ACCOUNT;
    }

    public int GetLength() =>
        Serializer.SizeOf(Address) +
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(Description);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Address);
        serializer.Write(PublicKey);
        serializer.Write(Description);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Address);
        serializer.Read(ref PublicKey);
        serializer.ReadN(ref Description);
        Console.WriteLine("account deser " + PublicKey);
    }
}
