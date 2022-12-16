using System.Numerics;
using System.Runtime.InteropServices;
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
}

public static class DifficultyExtensions
{
    public readonly static BigInteger TARGET_MAX = new BigInteger(new byte[32] {255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255}, true, true);

    public static BigInteger ToWork(this Difficulty difficulty)
    {
        return DifficultyExtensions.TARGET_MAX / difficulty.ToTarget();
    }

    public static BigInteger ToWork(this Difficulty difficulty, BigInteger target)
    {
        return DifficultyExtensions.TARGET_MAX / target;
    }

    public static BigInteger ToTarget(this Difficulty difficulty)
    {
        var exponent = difficulty.b0;
        var bytes = (TARGET_MAX >> exponent).ToByteArray();

        // work with big endian order
        if (BitConverter.IsLittleEndian) {
            Array.Reverse(bytes);
        }

        var fractionBytes = new byte[] {difficulty.b1, difficulty.b2, difficulty.b3, 0};

        if (BitConverter.IsLittleEndian) {
            Array.Reverse(fractionBytes);
        }

        var fraction = BitConverter.ToUInt32(fractionBytes) / (double)uint.MaxValue;

        fractionBytes = BitConverter.GetBytes(fraction);

        if (BitConverter.IsLittleEndian) {
            Array.Reverse(fractionBytes);
        }

        var offset = 1;
        for (int i = 0; i < 4; i++) {
            bytes[offset + i] = (byte)(bytes[offset + i] - fractionBytes[i]);
        }

        return new BigInteger(bytes, true, true);
    }

    public static Difficulty ToDifficulty(this BigInteger target)
    {
            var bytes = target.ToByteArray();
            Array.Resize(ref bytes, 4);

            if (BitConverter.IsLittleEndian) {
                Array.Reverse(bytes);
            }

            return new Difficulty {
                b0 = (byte)BigInteger.Log(target, 2),
                b1 = bytes[1],
                b2 = bytes[2],
                b3 = bytes[3]
            };
        }
}
