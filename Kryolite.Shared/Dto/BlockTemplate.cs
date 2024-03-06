namespace Kryolite.Shared.Dto;

public class BlockTemplate : ISerializable
{
    public long Height;
    public Address To;
    public ulong Value;
    public Difficulty Difficulty;
    public SHA256Hash Nonce;
    public SHA256Hash Solution;
    public long Timestamp;
    public SHA256Hash ParentHash;

    public BlockTemplate()
    {
        To = new();
        Nonce = new();
        Solution = new();
        ParentHash = new();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.BLOCKTEMPLATE;
    }

    public int GetLength() =>
        Serializer.SizeOf(Height) +
        Serializer.SizeOf(To) +
        Serializer.SizeOf(Value) +
        Serializer.SizeOf(Difficulty.Value) +
        Serializer.SizeOf(Nonce) +
        Serializer.SizeOf(Solution) +
        Serializer.SizeOf(Timestamp) +
        Serializer.SizeOf(ParentHash);

    public BlockTemplate Create<BlockTemplate>() where BlockTemplate : new()
    {
        return new BlockTemplate();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Height);
        serializer.Write(To);
        serializer.Write(Value);
        serializer.Write(Difficulty.Value);
        serializer.Write(Nonce);
        serializer.Write(Solution);
        serializer.Write(Timestamp);
        serializer.Write(ParentHash);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Height);
        serializer.Read(ref To);
        serializer.Read(ref Value);
        serializer.Read(ref Difficulty.Value);
        serializer.Read(ref Nonce);
        serializer.Read(ref Solution);
        serializer.Read(ref Timestamp);
        serializer.Read(ref ParentHash);
    }
}
