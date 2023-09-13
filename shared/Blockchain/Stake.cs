using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Validator
{
    [Key(0)]
    public long Stake { get; set; }
    [Key(1)]
    public Address RewardAddress { get; set; } = Address.NULL_ADDRESS;
    [Key(2)]
    public Stack<StakeHistory> StakeHistory = new();

    // Needed for rollbacks
    public void PopStake()
    {
        var lastState = StakeHistory.Pop();
        
        Stake = lastState.Stake;
        RewardAddress = lastState.RewardAddress;
    }

    public void PushStake(long stake, Address rewardAddress)
    {
        Stake = stake;
        RewardAddress = rewardAddress;

        StakeHistory.Push(new StakeHistory(stake, rewardAddress));
    }
}

[MessagePackObject]
public class StakeHistory
{
    [Key(0)]
    public long Stake { get; set; }
    [Key(1)]
    public Address RewardAddress { get; set; } = Address.NULL_ADDRESS;

    public StakeHistory(long amount, Address rewardAddress)
    {
        Stake = amount;
        RewardAddress = rewardAddress ?? throw new ArgumentNullException(nameof(rewardAddress));
    }
}