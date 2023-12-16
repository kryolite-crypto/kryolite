using Kryolite.EventBus;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Validator : EventBase
{
    [Key(0)]
    public Address NodeAddress { get; set; } = Address.NULL_ADDRESS;
    [Key(1)]
    public ulong Stake { get; set; }
    [Key(2)]
    public Address RewardAddress { get; set; } = Address.NULL_ADDRESS;

    public static ulong INACTIVE = 0; 
}
