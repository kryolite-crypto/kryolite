using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Stake
{
    [Key(0)]
    public long Amount { get; set; }
    [Key(1)]
    public Address RewardAddress { get; set; } = Address.NULL_ADDRESS;
    [Key(2)]
    public Stack<StakeHistory> StakeHistory = new();

    // Needed for rollbacks
    public void PopStake()
    {
        var lastState = StakeHistory.Pop();
        
        Amount = lastState.Amount;
        RewardAddress = lastState.RewardAddress;
    }

    public void PushStake(long stake, Address rewardAddress)
    {
        Amount = stake;
        RewardAddress = rewardAddress;

        StakeHistory.Push(new StakeHistory(stake, rewardAddress));
    }
}

[MessagePackObject]
public class StakeHistory
{
    [Key(0)]
    public long Amount { get; set; }
    [Key(1)]
    public Address RewardAddress { get; set; } = Address.NULL_ADDRESS;

    public StakeHistory(long amount, Address rewardAddress)
    {
        Amount = amount;
        RewardAddress = rewardAddress ?? throw new ArgumentNullException(nameof(rewardAddress));
    }
}