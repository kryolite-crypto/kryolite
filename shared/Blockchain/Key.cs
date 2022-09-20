using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Crypto.RIPEMD;

namespace Marccacoin.Shared;

public struct PrivateKey
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] 
    public byte[] Buffer;

    public static implicit operator byte[] (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator PrivateKey(byte[] buffer) => new PrivateKey { Buffer = buffer };
}

public struct PublicKey
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)]
    public byte[] Buffer;

    public static implicit operator byte[] (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator PublicKey(byte[] buffer) => new PublicKey { Buffer = buffer };

    public Address ToAddress()
    {
        using var sha256 = SHA256.Create();
        var shaHash = sha256.ComputeHash(Buffer);

        using var ripemd = new RIPEMD160Managed();
        var ripemdHash = ripemd.ComputeHash(shaHash);

        var addressBytes = ripemdHash.ToList();
        addressBytes.Insert(0, (byte)Network.MAIN); // network (161 mainnet, 177 testnet)
        addressBytes.Insert(1, 1); // version

        var ripemdBytes = new List<byte>(addressBytes);
        ripemdBytes.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        var h1 = sha256.ComputeHash(ripemdBytes.ToArray());
        var h2 = sha256.ComputeHash(h1);

        addressBytes.InsertRange(addressBytes.Count, h2.Take(4)); // checksum

        return addressBytes.ToArray();
    }
}
