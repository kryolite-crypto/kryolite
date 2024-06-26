using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct TransactionAddressKey
{
    private byte _start;

    public TransactionAddressKey(Address address, long id)
    {
        address.Buffer.CopyTo(this);
        id.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public TransactionAddressKey(Address address, ReadOnlySpan<byte> id)
    {
        address.Buffer.CopyTo(this);
        id.CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public const string KeyName = "ixTransactionAddress";
    public const int KeySize = 33;
}
