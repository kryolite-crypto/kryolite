using System.Runtime.InteropServices;

namespace Marccacoin;

public struct PrivateKey
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] 
    public byte[] Buffer;

    public static implicit operator byte[] (PrivateKey privateKey) => privateKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PrivateKey privateKey) => privateKey.Buffer;
    public static explicit operator PrivateKey(byte[] buffer) => new PrivateKey { Buffer = buffer };
}

public struct PublicKey
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)]
    public byte[] Buffer;

    public static implicit operator byte[] (PublicKey publicKey) => publicKey.Buffer;
    public static implicit operator ReadOnlySpan<byte> (PublicKey publicKey) => publicKey.Buffer;
    public static explicit operator PublicKey(byte[] buffer) => new PublicKey { Buffer = buffer };
}
