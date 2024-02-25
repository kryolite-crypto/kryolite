using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;

namespace Kryolite.Shared;

[DataContract]
[MemoryPackable]
public partial class Address
{
    [DataMember]
    public byte[] Buffer { get; set; }

    public Address()
    {
        Buffer = new byte[ADDRESS_SZ];
    }

    [MemoryPackConstructor]
    public Address(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length != ADDRESS_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public bool IsContract() => Buffer[0] == (byte)AddressType.CONTRACT;
    public bool IsWallet() => Buffer[0] == (byte)AddressType.WALLET;
    public override string ToString() => Constant.ADDR_PREFIX + Base32.Kryolite.Encode(Buffer);
    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static implicit operator byte[] (Address address) => address.Buffer;
    public static implicit operator Address(byte[] buffer) => new (buffer);
    public static implicit operator Address(Span<byte> buffer) => new(buffer.ToArray());
    public static implicit operator Address(string address) => new(Base32.Kryolite.Decode(address.Split(':').Last()));

    public override bool Equals(object? obj) 
    {
        return obj is Address c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public static bool operator ==(Address? a, Address? b)
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

        if (bytes.Length != 25)
        {
            return false;
        }

        var checksum = bytes.TakeLast(4).ToArray();
        var addr = bytes.Take(21).ToList();
        addr.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        var h1 = SHA256.HashData(addr.ToArray());
        var h2 = SHA256.HashData(h1);

        return Enumerable.SequenceEqual(h2.Take(4).ToArray(), checksum);
    }

    public const int ADDRESS_SZ = 25;
    public static readonly Address NULL_ADDRESS = new();
}