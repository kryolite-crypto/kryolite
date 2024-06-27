using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ValidatorKey
{
    private byte _start;

    public ValidatorKey(long height, Address address)
    {
        address.Buffer.CopyTo(this);
        height.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public const string KeyName = "Validator";
    public const int KeySize = Address.ADDRESS_SZ + sizeof(long);
}
