using System.Runtime.InteropServices;

namespace Marccacoin.Shared;

public record struct Nonce
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] 
    public byte[] Buffer;

    public static implicit operator byte[] (Nonce nonce) => nonce.Buffer;
    public static implicit operator Nonce (SHA256Hash hash) => new Nonce { Buffer = hash.Buffer };
}
