using System.Text.Json.Serialization;
using Kryolite.ByteSerializer;

namespace Kryolite.Shared;

public sealed class Token : ISerializable
{
    public ulong Id;
    public SHA256Hash TokenId;
    public bool IsConsumed;
    public Address Ledger;
    public Address Contract;
    public string Name;
    public string Description;

    public Token()
    {
        TokenId = new();
        Ledger = new();
        Contract = new();
        Name = string.Empty;
        Description = string.Empty;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.TOKEN;
    }

    public int GetLength() =>
        Serializer.SizeOf(Id) +
        Serializer.SizeOf(TokenId) +
        Serializer.SizeOf(IsConsumed) +
        Serializer.SizeOf(Ledger) +
        Serializer.SizeOf(Contract) +
        Serializer.SizeOf(Name) +
        Serializer.SizeOf(Description);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Id);
        serializer.Write(TokenId);
        serializer.Write(IsConsumed);
        serializer.Write(Ledger);
        serializer.Write(Contract);
        serializer.Write(Name);
        serializer.Write(Description);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Id);
        serializer.Read(ref TokenId);
        serializer.Read(ref IsConsumed);
        serializer.Read(ref Ledger);
        serializer.Read(ref Contract);
        serializer.Read(ref Name);
        serializer.Read(ref Description);
    }
}
