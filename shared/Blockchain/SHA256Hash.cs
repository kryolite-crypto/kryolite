using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using MessagePack;

namespace Marccacoin.Shared;

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
}

public static class SHA256HashExtensions
{
    public static BigInteger ToBigInteger(this SHA256Hash sha256)
    {
        return new BigInteger(sha256, true, true);
    }
}
