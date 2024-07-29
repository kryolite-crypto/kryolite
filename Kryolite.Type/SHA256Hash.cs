using System.Numerics;
using System.Runtime.CompilerServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type;

[SkipLocalsInit]
public sealed class SHA256Hash : IComparable<SHA256Hash>, ISerializable
{
    public byte[] Buffer => _buffer;

    private byte[] _buffer;
    private int _hashCode;

    public SHA256Hash()
    {
        _buffer = new byte[HASH_SZ];
        _hashCode = NULL_HASH.GetHashCode();
    }

    public SHA256Hash(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, HASH_SZ);

        _buffer = buffer;
        _hashCode = HashCodeHelper.CalculateHashCode(buffer);
    }

    public override string ToString() => Base32.Bech32.Encode(_buffer);
    public static explicit operator byte[] (SHA256Hash hash) => hash.Buffer;
    public static implicit operator ReadOnlySpan<byte> (SHA256Hash hash) => hash.Buffer;
    public static implicit operator SHA256Hash(byte[] buffer) => new(buffer);
    public static implicit operator SHA256Hash(string hash) => new(Base32.Bech32.Decode(hash));
    public static implicit operator SHA256Hash(Span<byte> buffer) => new(buffer.ToArray());

    public static bool operator ==(SHA256Hash a, SHA256Hash b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null)
        {
            return b is null;
        }

        return Enumerable.SequenceEqual(a.Buffer, b.Buffer);
    }

    public static bool operator !=(SHA256Hash a, SHA256Hash b)
    {
        return !(a == b);
    }

    public sealed override bool Equals(object? obj) 
    {
        return obj is SHA256Hash c && Enumerable.SequenceEqual(_buffer, c.Buffer);
    }

    public sealed override int GetHashCode()
    {
        return _hashCode;
    }

    public int CompareTo(SHA256Hash? other)
    {
        return MemoryExtensions.SequenceCompareTo(_buffer, (ReadOnlySpan<byte>)(other?.Buffer ?? []));
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.SHA256;
    }

    public int GetLength()
    {
        return HASH_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(_buffer, HASH_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref _buffer, HASH_SZ);
        _hashCode = _buffer.GetHashCode();
    }

    public BigInteger ToBigInteger()
    {
        return new BigInteger(_buffer, true, true);
    }

    public const int HASH_SZ = 32;

    public static readonly SHA256Hash NULL_HASH = new(new byte[HASH_SZ]);

    public static SHA256Hash Random
    {
        get
        {
            var hash = new SHA256Hash();
            System.Random.Shared.NextBytes(hash.Buffer);
            return hash;
        }
    }
}
