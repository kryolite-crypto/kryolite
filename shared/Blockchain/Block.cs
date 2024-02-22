using System.Runtime.Serialization;
using System.Security.Cryptography;
using MemoryPack;

namespace Kryolite.Shared.Blockchain;

[DataContract]
[MemoryPackable]
public partial class Block
{
    [DataMember]
    public Address To { get; set; } = Address.NULL_ADDRESS;

    [DataMember]
    public ulong Value { get; init; }

    [DataMember]
    public long Timestamp { get; init; }

    [DataMember]
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;

    [DataMember]
    public Difficulty Difficulty { get; set; }

    [DataMember]
    public SHA256Hash Nonce { get; set; } = SHA256Hash.NULL_HASH;

    private bool _isVerified = false;

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
        if (_isVerified)
        {
            return true;
        }

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
            _isVerified = true;
            return true;
        }

        return false;
    }
}
