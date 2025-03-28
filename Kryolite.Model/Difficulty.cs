using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Kryolite.Model;

[StructLayout(LayoutKind.Explicit)]
public partial record struct Difficulty
{
    [FieldOffset(0)]
    public uint Value;
    [FieldOffset(0)]
    public byte b0;
    [FieldOffset(1)]
    public byte b1;
    [FieldOffset(2)]
    public byte b2;
    [FieldOffset(3)]
    public byte b3;

    public static implicit operator uint (Difficulty difficulty) => difficulty.Value;

    public override string ToString()
    {
        return Math.Round(BigInteger.Log(this.ToWork(), 2), 4, MidpointRounding.ToZero).ToString(CultureInfo.InvariantCulture);
    }
}

public static class DifficultyExtensions
{
    public readonly static BigInteger TARGET_MAX = new BigInteger([255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255], true, true);
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

        var target = TARGET_MAX / (work + BigInteger.One);
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

