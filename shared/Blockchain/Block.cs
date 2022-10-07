using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LiteDB;
using Marccacoin.Shared;
using MessagePack;

namespace Marccacoin.Shared;

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
public class BlockHeader
{
    [Key(0)]
    [BsonId(false)]
    public long Id { get; set; }
    [Key(1)]
    public SHA256Hash ParentHash { get; set; }
    [Key(2)]
    public long Timestamp { get; set; }
    [Key(3)]
    public Nonce Nonce { get; set; }
    [Key(4)]
    public Difficulty Difficulty { get; set; }
}

[MessagePackObject]
public class Block
{
    [Key(0)]
    [BsonId(false)]
    public long Id { get; set; }
    [Key(1)]
    public BlockHeader Header { get; set; } = new();
    [Key(2)]
    public List<Transaction> Transactions { get; set; } = new List<Transaction>();

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(Header.ParentHash);
        stream.Write(new MerkleTree((List<Transaction>)Transactions).RootHash);
        stream.Write(BitConverter.GetBytes(Header.Difficulty.Value));
        stream.Write(BitConverter.GetBytes(Header.Timestamp));

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public bool VerifyNonce()
    {
        using var sha256 = SHA256.Create();

        var basehash = GetHash();
        var concat = basehash.Buffer.Concat(Header.Nonce.Buffer).ToArray();

        var hash = (SHA256Hash)sha256.ComputeHash(concat);

        var target = Header.Difficulty.ToTarget();
        var result = hash.ToBigInteger();

        return result.CompareTo(target) <= 0;
    }
}
