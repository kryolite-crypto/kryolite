using System.Runtime.CompilerServices;

namespace Kryolite.Node.Storage.Key;

[InlineArray(KeySize)]
public struct ScheduledTransactionKey
{
    private byte _start;

    public ScheduledTransactionKey(long timestamp, long id)
    {
        timestamp.ToKey().CopyTo(this);
        id.ToKey().CopyTo(this[8..]);
    }

    public const string KeyName = "ixScheduledTransaction";
    public const int KeySize = 16;
}
