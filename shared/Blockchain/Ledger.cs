using Kryolite.EventBus;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Ledger : EventBase
{
    [Key(0)]
    public Address Address { get; set; }
    [Key(1)]
    public ulong Balance { get; set; }
    [IgnoreMember]
    public ulong Pending { get; set; }

    public Ledger()
    {
        Address = Address.NULL_ADDRESS;
    }

    public Ledger(Address address)
    {
        Address = address;
    }
}
