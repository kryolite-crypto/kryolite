using System.Numerics;
using Kryolite.Node.Repository;
using Kryolite.Shared;

namespace Kryolite.Node.Procedure;

public class DifficultyScale
{
    private const int SCALE_MUL = 1_000;
    private const int SCALE_DIV = SCALE_MUL * SCALE_MUL;
    private readonly static BigInteger MAX_CHANGE = DifficultyExtensions.TARGET_MIN * 4;

    public static Difficulty Scale(ChainState chainState, int blocks, IStoreRepository repository)
    {
        var estimatedWork = chainState.CurrentDifficulty.ToWork();
        var actualWork = estimatedWork * blocks;
        
        var periodStart = Math.Max(1, chainState.Id - Constant.DIFFICULTY_LOOKBACK);
        var previousState = repository.GetChainState(periodStart - 1) ?? throw new Exception($"null state for height {periodStart - 1}, corrupted db?");

        // Calculate estimated work for the period and actual work done
        for (var i = periodStart; i < chainState.Id; i++)
        {
            var state = repository.GetChainState(i) ?? throw new Exception($"null state for height {i}, corrupted db?");

            actualWork += previousState.CurrentDifficulty.ToWork() * (state.Blocks - previousState.Blocks);
            estimatedWork += state.CurrentDifficulty.ToWork();

            previousState = state;
        }

        var currentTarget = chainState.CurrentDifficulty.ToWork();
        var newTarget = currentTarget * SCALE_MUL * (actualWork * SCALE_MUL / estimatedWork) / SCALE_DIV;

        return BigInteger.Max(BigInteger.Max(newTarget, currentTarget - MAX_CHANGE), DifficultyExtensions.TARGET_MIN).ToDifficulty();
    }
}
