using System.Runtime.CompilerServices;

namespace Kryolite.Shared;

public sealed class Effect : ISerializable
{
    public Address From;
    public Address To;
    public ulong Value;
    public Address Contract;
    public SHA256Hash? TokenId;
    public bool ConsumeToken;

    public Effect()
    {
        From = new();
        To = new();
        Contract = new();
    }

    public Effect(Address contract, Address from, Address to, ulong value, SHA256Hash? tokenId = null, bool consumeToken = false)
    {
        From = from;
        To = to;
        Contract = contract;
        Value = value;
        TokenId = tokenId;
        ConsumeToken = consumeToken;
    }

    public bool IsTokenEffect()
    {
        return TokenId is not null;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.EFFECT;
    }

    public int GetLength() =>
        Serializer.SizeOf(From) +
        Serializer.SizeOf(To) +
        Serializer.SizeOf(Value) +
        Serializer.SizeOf(Contract) +
        Serializer.SizeOfN(TokenId) +
        Serializer.SizeOf(ConsumeToken);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(From);
        serializer.Write(To);
        serializer.Write(Value);
        serializer.Write(Contract);
        serializer.WriteN(TokenId);
        serializer.Write(ConsumeToken);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref From);
        serializer.Read(ref To);
        serializer.Read(ref Value);
        serializer.Read(ref Contract);
        serializer.ReadN(ref TokenId);
        serializer.Read(ref ConsumeToken);
    }

    public void Fill(List<Effect> items, int count, ref Serializer serializer)
    {
        for (var i = 0; i < count; i++)
        {
            var item = new Effect();
            item.Serialize(ref serializer);
            items.Add(item);
        }
    }
}
