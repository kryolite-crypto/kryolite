using System.Runtime.CompilerServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type;

[SkipLocalsInit]
public sealed class PublicKey : ISerializable
{
    public byte[] Buffer => _buffer;

    private byte[] _buffer;
    private int _hashCode;

    public PublicKey()
    {
        _buffer = new byte[PUB_KEY_SZ];
        _hashCode = NULL_PUBLIC_KEY.GetHashCode();
    }

    public PublicKey(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, PUB_KEY_SZ);

        _buffer = buffer;
        _hashCode = HashCodeHelper.CalculateHashCode(buffer);
    }

    public override string ToString() => Base32.Bech32.Encode(_buffer);
    public static explicit operator byte[] (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator PublicKey(byte[] buffer) => new(buffer);
    public static implicit operator PublicKey(string pubKey) => new(Base32.Bech32.Decode(pubKey));

    public static bool operator ==(PublicKey a, PublicKey b)
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

    public static bool operator !=(PublicKey a, PublicKey b)
    {
        return !(a == b);
    }

    public override bool Equals(object? obj) 
    {
        return obj is PublicKey c && Enumerable.SequenceEqual(_buffer, c.Buffer);
    }

    public override int GetHashCode()
    {
        return _hashCode;
    }

    public Address ToAddress()
    {
        return Address.Create(_buffer);
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.PUBLIC_KEY;
    }

    public int GetLength()
    {
        return PUB_KEY_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(_buffer, PUB_KEY_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref _buffer, PUB_KEY_SZ);
        _hashCode = HashCodeHelper.CalculateHashCode(_buffer);
    }

    public const int PUB_KEY_SZ = 32;
    public static readonly PublicKey NULL_PUBLIC_KEY = new(new byte[PUB_KEY_SZ]);

    public static PublicKey Random
    {
        get
        {
            var publicKey = new PublicKey();
            System.Random.Shared.NextBytes(publicKey.Buffer);
            return publicKey;
        }
    }
}
