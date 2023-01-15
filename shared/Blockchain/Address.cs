using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MessagePack;
using SimpleBase;

namespace Kryolite.Shared;

[MessagePackObject]
public class Address
{
    [Key(0)]
    public byte[] Buffer { get; private init; } = new byte[ADDRESS_SZ];

    public Address(byte[] buffer)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (buffer.Length != ADDRESS_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public bool IsContract() => Buffer[1] == (byte)AddressType.CONTRACT;
    public bool IsWallet() => Buffer[1] == (byte)AddressType.WALLET;

    public override string ToString() => Constant.ADDR_PREFIX + Base58.Flickr.Encode(Buffer);

    public override bool Equals(object? obj) 
    {
        return obj is Address c && c.Buffer is not null && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
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

    public static bool operator ==(Address x, Address y) 
    {
        return x.Equals(y);
    }

    public static bool operator !=(Address x, Address y) 
    {
        return !x.Equals(y);
    }

    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static implicit operator byte[] (Address address) => address.Buffer;
    public static implicit operator Address(byte[] buffer) => new Address(buffer);
    public static implicit operator Address(Span<byte> buffer) => new Address(buffer.ToArray());
    public static implicit operator Address(string address) => IsValid(address) ? new Address(Base58.Flickr.Decode(address.Split(':').Last())) : throw new Exception($"invalid address {address}");

    public static bool IsValid(string address)
    {
        if (!address.StartsWith(Constant.ADDR_PREFIX)) {
            return false;
        }

        var bytes = Base58.Flickr.Decode(address.Split(':').Last());
        
        if (bytes.Length != 26) {
            return false;
        }

        var checksum = bytes.TakeLast(4).ToArray();
        var addr = bytes.Take(22).ToList();
        addr.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        using var sha256 = SHA256.Create();
        var h1 = sha256.ComputeHash(addr.ToArray());
        var h2 = sha256.ComputeHash(h1);

        return Enumerable.SequenceEqual(h2.Take(4).ToArray(), checksum);
    }

    public static int ADDRESS_SZ = 26;
}

public static class StringExtensions
{
    public static byte[] ToByteArray(this string str)
    {
        var bytes = new List<byte>();
        for(int i = 0; i < str.Length; i +=2)
        {
            var a = Convert.ToInt64(str.Substring(i, 2), 16);
            var b = Convert.ToChar(a);
            bytes.Add(Convert.ToByte(b));
        }

        return bytes.ToArray();
    }
}
