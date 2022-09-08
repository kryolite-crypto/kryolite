using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Marccacoin;

public struct SHA256Hash
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] 
    public byte[] Buffer;

    public override bool Equals(Object? obj) => obj is SHA256Hash c && this == c;
    public override int GetHashCode() => Buffer.GetHashCode();
    public static bool operator ==(SHA256Hash x, SHA256Hash y) => x.Buffer.SequenceEqual(y.Buffer);
    public static bool operator !=(SHA256Hash x, SHA256Hash y) => !(x.Buffer.SequenceEqual(y.Buffer));
}

public struct Nonce
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] 
    public byte[] Buffer;

    public override bool Equals(Object? obj) => obj is Nonce c && this == c;
    public override int GetHashCode() => Buffer.GetHashCode();
    public static bool operator ==(Nonce x, Nonce y) => x.Buffer.SequenceEqual(y.Buffer);
    public static bool operator !=(Nonce x, Nonce y) => !(x.Buffer.SequenceEqual(y.Buffer));
}

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

public class BlockHeader
{
    public ulong Id { get; set; }
    public SHA256Hash ParentHash { get; set; }
    public SHA256Hash RootHash { get; set; }
    public long Timestamp { get; set; }
    public Nonce Nonce { get; set; }
    public Difficulty Difficulty { get; set; }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(ParentHash.Buffer);
        stream.Write(RootHash.Buffer);
        stream.Write(BitConverter.GetBytes(Difficulty.Value));
        stream.Write(BitConverter.GetBytes(Timestamp));

        stream.Flush();
        stream.Position = 0;

        return new SHA256Hash {
            Buffer = sha256.ComputeHash(stream)
        };
    }

    public bool VerifyNonce()
    {
        using var sha256 = SHA256.Create();

        var basehash = GetHash();
        var concat = basehash.Buffer.Concat(Nonce.Buffer).ToArray();
        
        var headerhash = new SHA256Hash {
            Buffer = sha256.ComputeHash(concat)
        };

        var target = Difficulty.ToTarget();
        var result = headerhash.ToBigInteger();

        return result.CompareTo(target) <= 0;
    }
}

public class Block
{
    public BlockHeader Header { get; set; } = new();
}
