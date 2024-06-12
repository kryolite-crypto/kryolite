using System.Runtime.CompilerServices;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ChainStateKey
{
    private byte _start;

    public ChainStateKey(long height)
    {
        height.ToKey().CopyTo(this);
    }

    public const string KeyName = "ChainState";
    public const int KeySize = 8;
}
