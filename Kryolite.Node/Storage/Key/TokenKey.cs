using System.Runtime.CompilerServices;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct TokenKey
{
    private byte _start;

    public TokenKey(long id, long height)
    {
        id.ToKey().CopyTo(this);
        height.ToKey().CopyTo(this[8..]);
    }

    public const string KeyName = "Token";
    public const int KeySize = sizeof(long) + sizeof(long);
}
