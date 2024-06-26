using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ContractKey
{
    private byte _start;

    public ContractKey(Address address, long height)
    {
        address.Buffer.CopyTo(this);
        height.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public const string KeyName = "Contract";
    public const int KeySize = Address.ADDRESS_SZ + sizeof(long);
}
