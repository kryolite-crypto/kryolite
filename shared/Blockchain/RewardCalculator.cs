using Kryolite.Shared;

public static class RewardCalculator
{
    // private static int VIEWS_IN_YEAR = 500_000;
    // private static int FAIR_PERIOD = 1440; // 1 day
    private static readonly int VIEWS_IN_YEAR = 10_000;
    private static readonly int FAIR_PERIOD = 600;
    private static readonly double STEP_REDUCE = 100 * Constant.DECIMAL_MULTIPLIER;

    public static ulong BlockReward(long height)
    {
        if (height <= FAIR_PERIOD)
        {
            return Constant.DECIMAL_MULTIPLIER;
        }

        var steps = height / VIEWS_IN_YEAR;
        var reward = Constant.BLOCK_REWARD - (STEP_REDUCE * steps);
        return (ulong)Math.Max(reward, 0);
    }

    public static ulong ValidatorReward(long height)
    {
        if (height <= FAIR_PERIOD)
        {
            return Constant.DECIMAL_MULTIPLIER;
        }

        if (height >= (VIEWS_IN_YEAR * 10))
        {
            return Constant.VALIDATOR_REWARD / 2;
        }

        return Constant.VALIDATOR_REWARD;
    }

    public static ulong DevRewardForBlock(long height)
    {
        return BlockReward(height) / 100 * 2;
    }

    public static ulong DevRewardForValidator(long height)
    {
        return ValidatorReward(height) / 100 * 2;
    }
}
