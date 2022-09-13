using System.Runtime.InteropServices;

namespace Marccacoin;

public struct Address
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=26)] 
    public byte[] Buffer;

    public static implicit operator ReadOnlySpan<byte> (Address address) => address.Buffer;
    public static explicit operator Address(byte[] buffer) => new Address { Buffer = buffer };
}