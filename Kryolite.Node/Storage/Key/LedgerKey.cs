using System.Runtime.CompilerServices;
using Kryolite.Shared;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct LedgerKey
{
    private byte _start;

    public LedgerKey(long height, Address address)
    {
        address.Buffer.CopyTo(this);
        height.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
    }

    public const string KeyName = "Ledger";
    public const int KeySize = 33;
}
