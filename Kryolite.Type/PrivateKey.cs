using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Crypto.RIPEMD;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type;

[SkipLocalsInit]
public sealed class PrivateKey : ISerializable
{
    public byte[] Buffer => _buffer;

    private byte[] _buffer;
    private int _hashCode;

    public PrivateKey()
    {
        _buffer = new byte[PRIVATE_KEY_SZ];
        _hashCode = HashCodeHelper.CalculateHashCode(_buffer);
    }

    public PrivateKey(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, PRIVATE_KEY_SZ);

        _buffer = buffer;
        _hashCode = HashCodeHelper.CalculateHashCode(buffer);
    }

    public override string ToString() => Base32.ZBase32.Encode(_buffer);
    public static explicit operator byte[] (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator PrivateKey(byte[] buffer) => new(buffer);
    public static implicit operator PrivateKey(string privKey) => new(Base32.ZBase32.Decode(privKey));

    public static bool operator ==(PrivateKey a, PrivateKey b)
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

    public static bool operator !=(PrivateKey a, PrivateKey b)
    {
        return !(a == b);
    }

    public override bool Equals(object? obj) 
    {
        return obj is PrivateKey c && Enumerable.SequenceEqual(_buffer, c.Buffer);
    }

    public override int GetHashCode()
    {
        return _hashCode;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.PRIVATE_KEY;
    }

    public int GetLength()
    {
        return PRIVATE_KEY_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(_buffer, PRIVATE_KEY_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref _buffer, PRIVATE_KEY_SZ);
        _hashCode = _buffer.GetHashCode();
    }

    public const int PRIVATE_KEY_SZ = 32;
}

public static class HashCodeHelper
{
    public static int CalculateHashCode(ReadOnlySpan<byte> buffer)
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(buffer);
        return hashCode.ToHashCode();
    }
}