using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Signature : IComparable<Signature>
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

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static implicit operator byte[] (Signature signature) => signature.Buffer;
    public static implicit operator Span<byte> (Signature signature) => signature.Buffer;
    public static implicit operator ReadOnlySpan<byte> (Signature signature) => signature.Buffer;
    public static implicit operator Signature(byte[] buffer) => new(buffer);
    public static implicit operator Signature(string signature) => new(Base32.Kryolite.Decode(signature));

    public override bool Equals(object? obj) 
    {
        return obj is Signature c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public static bool operator ==(Signature a, Signature b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if ((a is null) || (b is null))
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

    public int CompareTo(Signature? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)Buffer, (ReadOnlySpan<byte>)(other?.Buffer ?? []));
    }

    public const int SIGNATURE_SZ = 64;
    public static readonly Signature NULL_SIGNATURE = new();
}
