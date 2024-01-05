using System.Numerics;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class SHA256Hash : IComparable<SHA256Hash>
{
    [Key(0)]
    public byte[] Buffer { get; private init; }

    public SHA256Hash()
    {
        Buffer = new byte[HASH_SZ];
    }

    public SHA256Hash(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length != HASH_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static implicit operator byte[] (SHA256Hash hash) => hash.Buffer;
    public static implicit operator ReadOnlySpan<byte> (SHA256Hash hash) => hash.Buffer;
    public static implicit operator SHA256Hash(byte[] buffer) => new(buffer);
    public static implicit operator SHA256Hash(string hash) => new(Base32.Kryolite.Decode(hash));
    public static implicit operator SHA256Hash(Span<byte> buffer) => new(buffer.ToArray());

    public override bool Equals(object? obj) 
    {
        return obj is SHA256Hash c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public static bool operator ==(SHA256Hash a, SHA256Hash b)
    {
        if (a is null || (b is null))
        {
            return false;
        }

        return a.Equals(b);
    }

    public static bool operator !=(SHA256Hash a, SHA256Hash b)
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

    public int CompareTo(SHA256Hash? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)Buffer, (ReadOnlySpan<byte>)(other?.Buffer ?? []));
    }

    public const int HASH_SZ = 32;

    public static readonly SHA256Hash NULL_HASH = new();
}

public static class SHA256HashExtensions
{
    public static BigInteger ToBigInteger(this SHA256Hash sha256)
    {
        return new BigInteger(sha256, true, true);
    }
}
