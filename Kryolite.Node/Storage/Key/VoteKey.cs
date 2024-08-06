using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct VoteKey
{
    private byte _start;

    public VoteKey(SHA256Hash blockhash)
    {
        blockhash.Buffer.CopyTo(this);
    }

    public const string KeyName = "Vote";
    public const int KeySize = SHA256Hash.HASH_SZ;
}
