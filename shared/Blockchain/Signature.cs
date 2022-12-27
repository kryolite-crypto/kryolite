using System.Runtime.InteropServices;
using MessagePack;

namespace Kryolite.Shared;

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

    public override bool Equals(object? obj) 
    {
        return obj is Signature c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
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

    public static bool operator ==(Signature x, Signature y) 
    {
        return Enumerable.SequenceEqual(x.Buffer, y.Buffer);
    }

    public static bool operator !=(Signature x, Signature y) 
    {
        return !(Enumerable.SequenceEqual(x.Buffer, y.Buffer));
    }
}
