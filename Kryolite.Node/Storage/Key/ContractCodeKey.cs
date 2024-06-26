using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ContractCodeKey
{
    private byte _start;

    public ContractCodeKey(Address address, long height)
    {
        address.Buffer.CopyTo(this);
        height.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public const string KeyName = "ContractCode";
    public const int KeySize = 33;
}
