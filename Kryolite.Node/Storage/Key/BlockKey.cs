using System.Runtime.CompilerServices;
using Kryolite.Shared;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct BlockKey
{
    private byte _start;

    public BlockKey(SHA256Hash blockhash)
    {
        blockhash.Buffer.CopyTo(this);
    }

    public const string KeyName = "Block";
    public const int KeySize = 32;
}
