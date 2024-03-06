using System.Runtime.CompilerServices;

namespace Kryolite.Shared;

[SkipLocalsInit]
public sealed class Signature : IComparable<Signature>, ISerializable
{
    public byte[] Buffer;

    public Signature()
    {
        Buffer = new byte[SIGNATURE_SZ];
    }

    public Signature(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, SIGNATURE_SZ);

        Buffer = buffer;
    }

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static explicit operator byte[] (Signature signature) => signature.Buffer;
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

        if (a is null)
        {
            return b is null;
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

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.SIGNATURE;
    }

    public int GetLength()
    {
        return SIGNATURE_SZ;
    }

    public Signature Create<Signature>() where Signature : new()
    {
        return new Signature();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Buffer, SIGNATURE_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Buffer, SIGNATURE_SZ);
    }

    public const int SIGNATURE_SZ = 64;
    public static readonly Signature NULL_SIGNATURE = new();
}
