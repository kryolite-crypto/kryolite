using System.Security.Cryptography;
using System.Text;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Address
{
    [Key(0)]
    public byte[] Buffer { get; private init; }

    public Address()
    {
        Buffer = new byte[ADDRESS_SZ];
    }

    public Address(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length != ADDRESS_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public bool IsContract() => Buffer[1] == (byte)AddressType.CONTRACT;
    public bool IsWallet() => Buffer[1] == (byte)AddressType.WALLET;
    public override string ToString() => Constant.ADDR_PREFIX + Base32.Kryolite.Encode(Buffer);
    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static implicit operator byte[] (Address address) => address.Buffer;
    public static implicit operator Address(byte[] buffer) => new(buffer);
    public static implicit operator Address(Span<byte> buffer) => new(buffer.ToArray());
    public static implicit operator Address(string address) => new(Base32.Kryolite.Decode(address.Split(':').Last()));

    public override bool Equals(object? obj) 
    {
        return obj is Address c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public static bool operator ==(Address? a, Address? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return a.Equals(b);
    }

    public static bool operator !=(Address a, Address b)
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

    public static bool IsValid(string address)
    {
        if (!address.StartsWith(Constant.ADDR_PREFIX))
        {
            return false;
        }

        var bytes = Base32.Kryolite.Decode(address.Split(':').Last());

        if (bytes.Length != 26)
        {
            return false;
        }

        var checksum = bytes.TakeLast(4).ToArray();
        var addr = bytes.Take(22).ToList();
        addr.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        var h1 = SHA256.HashData(addr.ToArray());
        var h2 = SHA256.HashData(h1);

        return Enumerable.SequenceEqual(h2.Take(4).ToArray(), checksum);
    }

    public const int ADDRESS_SZ = 26;
    public static readonly Address NULL_ADDRESS = new();
}
