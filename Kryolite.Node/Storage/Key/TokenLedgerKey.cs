using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct TokenLedgerKey
{
    private byte _start;

    public TokenLedgerKey(Address address, long id, long height)
    {
        address.Buffer.CopyTo(this);
        id.ToKey().CopyTo(this[Address.ADDRESS_SZ..]);
        height.ToKey().CopyTo(this[(Address.ADDRESS_SZ + sizeof(long))..]);
    }

    public const string KeyName = "ixTokenLedger";
    public const int KeySize = Address.ADDRESS_SZ + sizeof(long) + sizeof(long);
}
