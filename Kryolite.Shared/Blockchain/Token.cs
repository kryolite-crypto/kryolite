using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public sealed class Token : TokenBase, ISerializable
{
    public ulong Id;
    public SHA256Hash TokenId;
    public bool IsConsumed;
    public Address Ledger;
    public Address Contract;

    public Token()
    {
        TokenId = new();
        Ledger = new();
        Contract = new();
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
        Serializer.SizeOf(Contract);

    public Token Create<Token>() where Token : new()
    {
        return new Token();
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Write(Id);
        serializer.Write(TokenId);
        serializer.Write(IsConsumed);
        serializer.Write(Ledger);
        serializer.Write(Contract);
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Read(ref Id);
        serializer.Read(ref TokenId);
        serializer.Read(ref IsConsumed);
        serializer.Read(ref Ledger);
        serializer.Read(ref Contract);
    }
}

public class TokenBase
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
