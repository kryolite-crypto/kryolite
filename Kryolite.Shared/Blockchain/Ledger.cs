using Kryolite.EventBus;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class Ledger : EventBase
{
    public Address Address { get; set; }
    public ulong Balance { get; set; }
    public bool Locked { get; set; }
    public ulong Pending { get; set; }

    [MemoryPackConstructor]
    public Ledger()
    {
        Address = Address.NULL_ADDRESS;
    }

    public Ledger(Address address)
    {
        Address = address;
    }
}
