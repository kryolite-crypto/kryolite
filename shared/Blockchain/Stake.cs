using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Stake
{
    [Key(0)]
    public long Amount { get; set; }
    [Key(1)]
    public Address RewardAddress { get; set; } = new();
}
