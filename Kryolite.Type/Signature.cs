using System.Runtime.CompilerServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type;

[SkipLocalsInit]
public sealed class Signature : IComparable<Signature>, ISerializable
{
    public byte[] Buffer => _buffer;

    private byte[] _buffer;
    private int _hashCode;

    public Signature()
    {
        _buffer = new byte[SIGNATURE_SZ];
        _hashCode = NULL_SIGNATURE.GetHashCode();
    }

    public Signature(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, SIGNATURE_SZ);

        _buffer = buffer;
        _hashCode = HashCodeHelper.CalculateHashCode(buffer);
    }

    public override string ToString() => Base32.Bech32.Encode(_buffer);
    public static explicit operator byte[] (Signature signature) => signature.Buffer;
    public static implicit operator Span<byte> (Signature signature) => signature.Buffer;
    public static implicit operator ReadOnlySpan<byte> (Signature signature) => signature.Buffer;
    public static implicit operator Signature(byte[] buffer) => new(buffer);
    public static implicit operator Signature(string signature) => new(Base32.Bech32.Decode(signature));

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

        return Enumerable.SequenceEqual(a.Buffer, b.Buffer);
    }

    public static bool operator !=(Signature a, Signature b)
    {
        return !(a == b);
    }

    public override bool Equals(object? obj) 
    {
        return obj is Signature c && Enumerable.SequenceEqual(_buffer, c.Buffer);
    }

    public override int GetHashCode()
    {
        return _hashCode;
    }

    public int CompareTo(Signature? other)
    {
        return MemoryExtensions.SequenceCompareTo(_buffer, (ReadOnlySpan<byte>)(other?.Buffer ?? []));
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
        serializer.Write(_buffer, SIGNATURE_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref _buffer, SIGNATURE_SZ);
        _hashCode = _buffer.GetHashCode();
    }

    public const int SIGNATURE_SZ = 64;
    public static readonly Signature NULL_SIGNATURE = new(new byte[SIGNATURE_SZ]);
}
