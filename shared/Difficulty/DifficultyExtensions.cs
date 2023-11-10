using System.Numerics;

namespace Kryolite.Shared;

public static class DifficultyExtensions
{
    public readonly static BigInteger TARGET_MAX = new BigInteger(new byte[32] {255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255}, true, true);
    public readonly static BigInteger TARGET_MIN = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

    public static BigInteger ToTarget(this Difficulty difficulty)
    {
        var bytes = (TARGET_MAX >>> difficulty.b0).ToByteArray();
        var targetPrefix = new byte[] { difficulty.b3, difficulty.b2, difficulty.b1 };

        Array.Reverse(bytes);

        for (int i = 0; i < targetPrefix.Length; i++)
        {
            bytes[i] = targetPrefix[i];
        }

        return new BigInteger(bytes, true, true);
    }

    public static BigInteger ToWork(this Difficulty difficulty)
    {
        return TARGET_MAX / difficulty.ToTarget();
    }

    public static Difficulty ToDifficulty(this BigInteger work)
    {
        var exponent = (byte)BigInteger.Log(work, 2);

        var target = (TARGET_MAX / (work + BigInteger.One));
        var bytes = target.ToByteArray();

        Array.Reverse(bytes);

        return new Difficulty
        {
            b0 = exponent,
            b1 = bytes[2],
            b2 = bytes[1],
            b3 = bytes[0]
        };
    }
}
