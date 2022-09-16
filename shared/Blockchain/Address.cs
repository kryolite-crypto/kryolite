using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Marccacoin.Shared;

public struct Address
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=26)] 
    public byte[] Buffer;

    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static implicit operator byte[] (Address address) => address.Buffer;
    public static implicit operator Address(byte[] buffer) => new Address { Buffer = buffer };
    public static implicit operator Address(string address) => IsValid(address) ? new Address { Buffer = address.Split('x').Last().ToByteArray() } : throw new Exception($"invalid address {address}");

    public static bool IsValid(string address)
    {
        if (!address.StartsWith("FIM0x")) {
            return false;
        }

        var bytes = address.Split('x').Last().ToByteArray();
        
        if (bytes.Length != 26) {
            return false;
        }

        var checksum = bytes.TakeLast(4).ToArray();
        var addr = bytes.Take(22).ToList();
        addr.InsertRange(0, Encoding.ASCII.GetBytes("FIM0x"));

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
