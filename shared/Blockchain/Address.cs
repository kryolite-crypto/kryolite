using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public struct Address
{
    [Key(0)]
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=26)] 
    public byte[] Buffer;

    public override string ToString()
    {
        return Constant.ADDR_PREFIX + BitConverter.ToString(Buffer).Replace("-", "");
    }

    public override bool Equals(object? obj) 
    {
        return obj is Address c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
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
        return Enumerable.SequenceEqual(x.Buffer, y.Buffer);
    }

    public static bool operator !=(Address x, Address y) 
    {
        return !(Enumerable.SequenceEqual(x.Buffer, y.Buffer));
    }

    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static implicit operator byte[] (Address address) => address.Buffer;
    public static implicit operator Address(byte[] buffer) => new Address { Buffer = buffer };
    public static implicit operator Address(string address) => IsValid(address) ? new Address { Buffer = address.Split('x').Last().ToByteArray() } : throw new Exception($"invalid address {address}");

    public static bool IsValid(string address)
    {
        if (!address.StartsWith(Constant.ADDR_PREFIX)) {
            return false;
        }

        var bytes = address.Split('x').Last().ToByteArray();
        
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
