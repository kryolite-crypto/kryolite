using System.Runtime.InteropServices;
using MessagePack;

namespace Marccacoin.Shared;

[MessagePackObject]
public record struct Nonce
{
    [Key(0)]
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] 
    public byte[] Buffer;

    public static implicit operator byte[] (Nonce nonce) => nonce.Buffer;
    public static implicit operator Nonce (SHA256Hash hash) => new Nonce { Buffer = hash.Buffer };
    public static implicit operator Nonce (byte[] nonce) => new Nonce { Buffer = nonce };
}
