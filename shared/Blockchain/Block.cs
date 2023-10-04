using Kryolite.Shared.Dto;
using MessagePack;
using System.Collections.Immutable;
using System.Reactive;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Block
{
    [Key(0)]
    public Address To { get; set; } = Address.NULL_ADDRESS;
    [Key(1)]
    public long Value { get; init; }
    [Key(2)]
    public long Timestamp { get; init; }
    [Key(3)]
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;
    [Key(4)]
    public Difficulty Difficulty { get; set; }
    [Key(5)]
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
            Buffer = basehash.Buffer.Concat(Nonce.Buffer ?? new byte[0]).ToArray()
        };

        var hash = Grasshopper.Hash(LastHash, concat);

        var target = Difficulty.ToTarget();
        var result = hash.ToBigInteger();

        return result.CompareTo(target) <= 0;
    }
}
