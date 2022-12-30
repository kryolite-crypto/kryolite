using System.Numerics;
using System.Runtime.InteropServices;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public struct SHA256Hash
{
    [Key(0)]
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)]
    public byte[] Buffer;

    public SHA256Hash()
    {
        Buffer = new byte[32];
    }

    public static implicit operator byte[] (SHA256Hash hash) => hash.Buffer;
    public static implicit operator ReadOnlySpan<byte> (SHA256Hash hash) => hash.Buffer;
    public static implicit operator SHA256Hash(byte[] buffer) => new SHA256Hash { Buffer = buffer };

    public override bool Equals(object? obj) 
    {
        return obj is SHA256Hash c && c.Buffer is not null && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var b in Buffer)
        {
            hash = hash * 31 + b.GetHashCode();
        }
        return hash;
    }

    public static bool operator ==(SHA256Hash x, SHA256Hash y) 
    {
        return x.Equals(y);
    }

    public static bool operator !=(SHA256Hash x, SHA256Hash y) 
    {
        return !x.Equals(y);
    }
}

public static class SHA256HashExtensions
{
    public static BigInteger ToBigInteger(this SHA256Hash sha256)
    {
        return new BigInteger(sha256, true, true);
    }
}
