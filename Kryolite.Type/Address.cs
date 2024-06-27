using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type;

[SkipLocalsInit]
public sealed class Address : ISerializable
{
    public byte[] Buffer => _buffer;

    private byte[] _buffer;
    private int _hashCode;

    public Address()
    {
        _buffer = new byte[ADDRESS_SZ];
        _hashCode = NULL_ADDRESS.GetHashCode();
    }

    public Address(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, ADDRESS_SZ);

        _buffer = buffer;
        _hashCode = HashCodeHelper.CalculateHashCode(buffer);
    }

    public bool IsContract() => Buffer[0] == (byte)AddressType.CONTRACT;
    public bool IsWallet() => Buffer[0] == (byte)AddressType.WALLET;

    public override string ToString() => ADDR_PREFIX + Base32.Bech32.Encode(Buffer);
    public static explicit operator byte[] (Address address) => address.Buffer;
    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static implicit operator Address(byte[] buffer) => new (buffer);
    public static implicit operator Address(Span<byte> buffer) => new(buffer.ToArray());
    public static implicit operator Address(string address) => new(Base32.Bech32.Decode(address.Split(':').Last()));

    public static bool operator ==(Address a, Address b)
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

    public static bool operator !=(Address a, Address b)
    {
        return !(a == b);
    }

    public override bool Equals(object? obj) 
    {
        return obj is Address c && Enumerable.SequenceEqual(_buffer, c.Buffer);
    }
    public override int GetHashCode()
    {
        return _hashCode;
    }

    public static Address Create(PublicKey publicKey)
    {
        byte[] addressBytes = [(byte)AddressType.WALLET, ..publicKey.Buffer];
        byte[] prefixConcat = [..Encoding.ASCII.GetBytes(ADDR_PREFIX), ..addressBytes];

        var checksum = SHA256.HashData(prefixConcat);

        return new Address([..addressBytes, ..checksum[0..2]]);
    }

    public static bool IsValid(string address)
    {
        if (!address.StartsWith(ADDR_PREFIX))
        {
            return false;
        }

        var bytes = Base32.Bech32.Decode(address.Split(':').Last());

        if (bytes.Length != ADDRESS_SZ)
        {
            return false;
        }

        var checksum = bytes[^2..];

        byte[] addressBytes = bytes[0..^2];
        byte[] prefixConcat = [..Encoding.ASCII.GetBytes(ADDR_PREFIX), ..addressBytes];

        var h1 = SHA256.HashData(prefixConcat);

        return Enumerable.SequenceEqual(h1[0..2], checksum);
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.ADDRESS;
    }

    public int GetLength()
    {
        return ADDRESS_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(_buffer, ADDRESS_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref _buffer, ADDRESS_SZ);
        _hashCode = HashCodeHelper.CalculateHashCode(_buffer);
    }

    public const int ADDRESS_SZ = 35;
    public const string ADDR_PREFIX = "kryo:";

    public static readonly Address NULL_ADDRESS = new(new byte[ADDRESS_SZ]);
}
