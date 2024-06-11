using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kryolite.Shared;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct TransactionIdKey
{
    private byte _start;

    public TransactionIdKey(SHA256Hash txid)
    {
        txid.Buffer.CopyTo(this);
    }

    public const string KeyName = "ixTransactionId";
    public const int KeySize = 32;
}
