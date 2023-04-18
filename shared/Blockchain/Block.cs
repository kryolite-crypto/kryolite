using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MessagePack;

namespace Kryolite.Shared;

[StructLayout(LayoutKind.Explicit)]
public struct Concat
{
    [FieldOffset(0)]
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=64)] 
    public byte[] Buffer;

    public override bool Equals(Object? obj) => obj is Concat c && this == c;
    public override int GetHashCode() => Buffer.GetHashCode();
    public static bool operator ==(Concat x, Concat y) => x.Buffer.SequenceEqual(y.Buffer);
    public static bool operator !=(Concat x, Concat y) => !(x.Buffer.SequenceEqual(y.Buffer));
}

[MessagePackObject]
public class PowBlock
{
    [IgnoreMember]
    [JsonIgnore]
    public Guid Id { get; set; }
    [IgnoreMember]
    public Guid PosBlockId { get; set; }

    [Key(0)]
    public long Height { get; set; }
    [Key(1)]
    public SHA256Hash ParentHash { get; set; }
    [Key(2)]
    public long Timestamp { get; set; }
    [Key(3)]
    public Nonce Nonce { get; set; }
    [Key(4)]
    public Difficulty Difficulty { get; set; }
    [Key(5)]
    public List<Transaction> Transactions { get; set; } = new();

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(ParentHash);
        stream.Write(new MerkleTree(Transactions).RootHash);
        stream.Write(BitConverter.GetBytes(Difficulty.Value));
        stream.Write(BitConverter.GetBytes(Timestamp));

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public bool VerifyNonce()
    {
        var basehash = GetHash();
        var concat = new Concat
        {
            Buffer = basehash.Buffer.Concat(Nonce.Buffer).ToArray()
        };

        var hash = KryoHash2.Hash(concat);

        var target = Difficulty.ToTarget();
        var result = hash.ToBigInteger();

        return result.CompareTo(target) <= 0;
    }
}

[MessagePackObject]
public class PosBlock
{
    [IgnoreMember]
    [JsonIgnore]
    public Guid Id { get; set; }

    [Key(0)]
    public long Height { get; set; }
    [Key(1)]
    public SHA256Hash ParentHash { get; set; }
    [Key(2)]
    public long Timestamp { get; set; }
    [Key(3)]
    public PowBlock? Pow { get; set; }
    [Key(4)]
    public List<Transaction> Transactions { get; set; } = new();
    [Key(5)]
    public PublicKey SignedBy { get; set; }
    [Key(6)]
    public Signature Signature { get; set; }
    [Key(7)]
    public List<Vote> Votes { get; set; } = new();

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(ParentHash);
        stream.Write(BitConverter.GetBytes(Timestamp));
        
        if (Pow is not null) {
            stream.Write(Pow.GetHash());
        }
        
        stream.Write(new MerkleTree(Transactions).RootHash);

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }
}

[MessagePackObject]
public class KeyAndSignature
{
    [Key(0)]
    public SHA256Hash RootHash { get; set; }
    [Key(1)]
    public PublicKey PublicKey { get; set; }
    [Key(2)]
    public Signature Signature { get; set; }
}