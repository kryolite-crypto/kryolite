using System.Runtime.Serialization;
using System.Security.Cryptography;
using MemoryPack;

namespace Kryolite.Shared.Blockchain;

[MemoryPackable]
public partial class Block
{
    public Address To { get; set; } = Address.NULL_ADDRESS;
    public ulong Value { get; init; }
    public long Timestamp { get; init; }
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;
    public Difficulty Difficulty { get; set; }
    public SHA256Hash Nonce { get; set; } = SHA256Hash.NULL_HASH;

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
}
