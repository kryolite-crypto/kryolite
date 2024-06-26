using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ViewHashKey
{
    private byte _start;

    public ViewHashKey(SHA256Hash viewhash)
    {
        viewhash.Buffer.CopyTo(this);
    }

    public const string KeyName = "ixViewHash";
    public const int KeySize = 32;
}
