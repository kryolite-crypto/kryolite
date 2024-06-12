using System.Runtime.CompilerServices;
using Kryolite.Shared;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ContractSnapshotKey
{
    private byte _start;

    public ContractSnapshotKey(Address address, long height)
    {
        address.Buffer.CopyTo(this);
        height.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public const string KeyName = "ContractSnapshot";
    public const int KeySize = 33;
}
