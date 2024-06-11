using System.Runtime.CompilerServices;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct TransactionKey
{
    private byte _start;

    public TransactionKey(long id)
    {
        id.ToKey().CopyTo(this);
    }

    public const string KeyName = "Transaction";
    public const int KeySize = 8;
}
