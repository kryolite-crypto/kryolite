using System.Runtime.InteropServices;
using MessagePack;

namespace Marccacoin.Shared;

[MessagePackObject]
public struct Signature
{
    [Key(0)]
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=64)] 
    public byte[] Buffer;

    public Signature()
    {
        Buffer = new byte[64];
    }

    public static implicit operator byte[] (Signature signature) => signature.Buffer;
    public static implicit operator Span<byte> (Signature signature) => signature.Buffer;
    public static implicit operator ReadOnlySpan<byte> (Signature signature) => signature.Buffer;
    public static implicit operator Signature(byte[] buffer) => new Signature { Buffer = buffer };
}
