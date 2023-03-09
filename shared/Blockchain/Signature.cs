using System.Runtime.InteropServices;
using MessagePack;
using SimpleBase;

namespace Kryolite.Shared;

[MessagePackObject]
public class Signature
{
    [Key(0)]
    public byte[] Buffer { get; private init; }

    public Signature()
    {
        Buffer = new byte[SIGNATURE_SZ];
    }

    public Signature(byte[] buffer)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (buffer.Length != SIGNATURE_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public override string ToString() => Base58.Flickr.Encode(Buffer);
    public static implicit operator byte[] (Signature signature) => signature.Buffer;
    public static implicit operator Span<byte> (Signature signature) => signature.Buffer;
    public static implicit operator ReadOnlySpan<byte> (Signature signature) => signature.Buffer;
    public static implicit operator Signature(byte[] buffer) => new Signature { Buffer = buffer };
    public static implicit operator Signature(string signature) => new Signature { Buffer = Base58.Flickr.Decode(signature) };

    public override bool Equals(object? obj) 
    {
        return obj is Signature c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public static bool operator ==(Signature a, Signature b)
    {
        if (System.Object.ReferenceEquals(a, b))
        {
            return true;
        }

        if (((object)a == null) || ((object)b == null))
        {
            return false;
        }

        return a.Equals(b);
    }

    public static bool operator !=(Signature a, Signature b)
    {
        return !(a == b);
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

    public static int SIGNATURE_SZ = 64;
}
