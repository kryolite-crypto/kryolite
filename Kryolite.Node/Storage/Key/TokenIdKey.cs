using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct TokenIdKey
{
    private byte _start;

    public TokenIdKey(Address contract, SHA256Hash tokenId, long height)
    {
        contract.Buffer.CopyTo(this);
        tokenId.Buffer.CopyTo(this[Address.ADDRESS_SZ..]);
        height.ToKey().CopyTo(this[(Address.ADDRESS_SZ + SHA256Hash.HASH_SZ)..]);
    }

    public const string KeyName = "ixTokenId";
    public const int KeySize = Address.ADDRESS_SZ + SHA256Hash.HASH_SZ + sizeof(long);
}
