using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

public sealed class Block : ISerializable
{
    public Address To;
    public ulong Value;
    public long Timestamp;
    public SHA256Hash LastHash;
    public Difficulty Difficulty;
    public SHA256Hash Nonce;

    public Block()
    {
        To = new();
        LastHash = new();
        Nonce = new();
    }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(LastHash);
        stream.Write(BitConverter.GetBytes(Difficulty.Value));
        stream.Write(Nonce);

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public SHA256Hash GetBaseHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(LastHash);
        stream.Write(BitConverter.GetBytes(Difficulty.Value));

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public bool VerifyNonce()
    {
        var basehash = GetBaseHash();
        var concat = new Concat
        {
            Buffer = basehash.Buffer.Concat(Nonce.Buffer ?? []).ToArray()
        };

        var hash = Grasshopper.Hash(concat);

        var target = Difficulty.ToTarget();
        var result = hash.ToBigInteger();

        if(result.CompareTo(target) <= 0)
        {
            return true;
        }

        return false;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.BLOCK;
    }

    public int GetLength() =>
        Serializer.SizeOf(To) +
        Serializer.SizeOf(Value) +
        Serializer.SizeOf(Timestamp) +
        Serializer.SizeOf(LastHash) +
        Serializer.SizeOf(Difficulty.Value) +
        Serializer.SizeOf(Nonce);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(To);
        serializer.Write(Value);
        serializer.Write(Timestamp);
        serializer.Write(LastHash);
        serializer.Write(Difficulty.Value);
        serializer.Write(Nonce);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref To);
        serializer.Read(ref Value);
        serializer.Read(ref Timestamp);
        serializer.Read(ref LastHash);
        serializer.Read(ref Difficulty.Value);
        serializer.Read(ref Nonce);
    }
}
