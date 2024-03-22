using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Crypto.RIPEMD;
using Kryolite.ByteSerializer;

namespace Kryolite.Shared;

public sealed class PrivateKey : ISerializable
{
    public byte[] Buffer;

    public PrivateKey()
    {
        Buffer = new byte[PRIVATE_KEY_SZ];
    }

    public PrivateKey(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, PRIVATE_KEY_SZ);

        Buffer = buffer;
    }

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static explicit operator byte[] (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator PrivateKey(byte[] buffer) => new(buffer);
    public static implicit operator PrivateKey(string privKey) => new(Base32.Kryolite.Decode(privKey));

    public override bool Equals(object? obj) 
    {
        return obj is PrivateKey c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

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

        return a.Equals(b);
    }

    public static bool operator !=(PrivateKey a, PrivateKey b)
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
        serializer.Write(Buffer, PRIVATE_KEY_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Buffer, PRIVATE_KEY_SZ);
    }

    public const int PRIVATE_KEY_SZ = 32;
}

[SkipLocalsInit]
public sealed class PublicKey : ISerializable
{
    public byte[] Buffer;

    public PublicKey()
    {
        Buffer = new byte[PUB_KEY_SZ];
    }

    public PublicKey(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNotEqual(buffer.Length, PUB_KEY_SZ);

        Buffer = buffer;
    }

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static explicit operator byte[] (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator PublicKey(byte[] buffer) => new(buffer);
    public static implicit operator PublicKey(string pubKey) => new(Base32.Kryolite.Decode(pubKey));

    public override bool Equals(object? obj) 
    {
        return obj is PublicKey c && Enumerable.SequenceEqual(Buffer, c.Buffer);
    }

    public static bool operator ==(PublicKey? a, PublicKey? b)
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

    public static bool operator !=(PublicKey? a, PublicKey? b)
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

    public Address ToAddress()
    {
        var shaHash = SHA256.HashData(Buffer);

        using var ripemd = new RIPEMD160Managed();
        var ripemdHash = ripemd.ComputeHash(shaHash);

        byte[] addressBytes = [(byte)AddressType.WALLET, ..ripemdHash];
        byte[] prefixConcat = [..Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX), ..addressBytes];

        var h1 = SHA256.HashData(prefixConcat);
        var h2 = SHA256.HashData(h1);

        return (Address)(byte[])[..addressBytes, ..h2[0..4]];
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
        serializer.Write(Buffer, PUB_KEY_SZ);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Buffer, PUB_KEY_SZ);
    }

    public const int PUB_KEY_SZ = 32;
    public static readonly PublicKey NULL_PUBLIC_KEY = new();
}
