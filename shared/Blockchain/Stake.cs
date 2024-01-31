using Kryolite.EventBus;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class Validator : EventBase
{
    public Address NodeAddress { get; set; } = Address.NULL_ADDRESS;

    public ulong Stake { get; set; }

    public Address RewardAddress { get; set; } = Address.NULL_ADDRESS;

    public static ulong INACTIVE = 0; 
}
