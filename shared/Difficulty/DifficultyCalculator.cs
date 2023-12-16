using System.Numerics;

namespace Kryolite.Shared;

public static class DifficultyCalculator
{
    public static Difficulty ScaleDifficulty(this Difficulty difficulty, int blocks, BigInteger previousTarget)
    {
        var currentTarget = difficulty.ToWork();
        var totalWork = (currentTarget * blocks) + previousTarget;
        var maxChange = DifficultyExtensions.TARGET_MIN * 4;

        var delta = BigInteger.Max(totalWork - currentTarget, -maxChange);
        var newTarget = currentTarget + BigInteger.Min(delta, maxChange);

        return BigInteger.Max(DifficultyExtensions.TARGET_MIN, newTarget).ToDifficulty();
    }
}
