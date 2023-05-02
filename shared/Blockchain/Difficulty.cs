using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
[StructLayout(LayoutKind.Explicit)]
public record struct Difficulty
{
    [Key(0)]
    [FieldOffset(0)]
    public uint Value;
    [IgnoreMemberAttribute]
    [FieldOffset(0)]
    public byte b0;
    [IgnoreMemberAttribute]
    [FieldOffset(1)]
    public byte b1;
    [IgnoreMemberAttribute]
    [FieldOffset(2)]
    public byte b2;
    [IgnoreMemberAttribute]
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
    public readonly static BigInteger TARGET_MAX = new BigInteger(new byte[32] {255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255}, true, true);

    public static BigInteger ToTarget(this Difficulty difficulty)
    {
        var bytes = (TARGET_MAX >>> difficulty.b0).ToByteArray();
        var targetPrefix = new byte[] { difficulty.b3, difficulty.b2, difficulty.b1 };

        Array.Reverse(bytes);
        Array.Copy(targetPrefix, 0, bytes, 1, targetPrefix.Length);

        return new BigInteger(bytes, true, true);
    }

    public static BigInteger ToWork(this Difficulty difficulty)
    {
        var exponent = difficulty.b0;
        var remainderBytes = new byte[] { difficulty.b1, difficulty.b2, difficulty.b3 };

        // Pad with zeroes to get correct scale
        Array.Reverse(remainderBytes);
        Array.Resize(ref remainderBytes, (exponent / 8) + 1);

        // Note: remainderBytes passed as BigEndian format
        var remainder = new BigInteger(remainderBytes, false, true);

        return BigInteger.Pow(2, exponent) + remainder;
    }

    public static Difficulty ToDifficulty(this BigInteger target)
    {
        var exponent = (byte)BigInteger.Log(target, 2);
        var remaining = target - BigInteger.Pow(2, exponent);

        var bytes = remaining.ToByteArray();

        Array.Reverse(bytes);
        Array.Resize(ref bytes, 4);

        return new Difficulty
        {
            b0 = exponent,
            b1 = bytes[2],
            b2 = bytes[1],
            b3 = bytes[0]
        };
    }
}
