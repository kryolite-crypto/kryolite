using System.Runtime.CompilerServices;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ViewKey
{
    private byte _start;

    public ViewKey(long height)
    {
        height.ToKey().CopyTo(this);
    }

    public const string KeyName = "View";
    public const int KeySize = 8;
}
