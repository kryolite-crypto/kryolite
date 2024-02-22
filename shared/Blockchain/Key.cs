using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Crypto.RIPEMD;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class PrivateKey
{
    public byte[] Buffer { get; private init; }

    public PrivateKey()
    {
        Buffer = new byte[PRIVATE_KEY_SZ];
    }

    [MemoryPackConstructor]
    public PrivateKey(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length != PRIVATE_KEY_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static implicit operator byte[] (PrivateKey privateKey) => privateKey.Buffer;
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

    public const int PRIVATE_KEY_SZ = 32;
}

[DataContract]
[MemoryPackable]
public partial class PublicKey
{
    [DataMember]
    public byte[] Buffer { get; set; }

    public PublicKey()
    {
        Buffer = new byte[PUB_KEY_SZ];
    }

    [MemoryPackConstructor]
    public PublicKey(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length != PUB_KEY_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public override string ToString() => Base32.Kryolite.Encode(Buffer);
    public static implicit operator byte[] (PublicKey publicKey) => publicKey.Buffer;
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

    public const int PUB_KEY_SZ = 32;
    public static readonly PublicKey NULL_PUBLIC_KEY = new();
}
