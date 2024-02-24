using System.Numerics;
using Kryolite.Node.Repository;
using Kryolite.Shared;

namespace Kryolite.Node.Procedure;

public class DifficultyScale
{
    private const int SCALE_MUL = 1_000;
    private const int SCALE_DIV = SCALE_MUL * SCALE_MUL;
    public static Difficulty Scale(ChainState chainState, IStoreRepository repository)
    {
        var periodStart = Math.Max(0, chainState.Id - Constant.DIFFICULTY_LOOKBACK);
        var startState = repository.GetChainState(periodStart) ?? throw new Exception($"could not load ChainState at height {chainState.Id}, corrupted db?");

        var expectedWork = chainState.CurrentDifficulty.ToWork();
        var actualWork = (chainState.TotalWork - startState.TotalWork) / Constant.EXPECTED_BLOCKS;

        // Clamp factor to [0.75x ... 10x]
        var factor = BigInteger.Clamp(actualWork * SCALE_MUL / expectedWork, 750, 10000);
        var newTarget = expectedWork * SCALE_MUL * factor / SCALE_DIV;

        return BigInteger.Max(newTarget, DifficultyExtensions.TARGET_MIN).ToDifficulty();
    }
}
