using System.Globalization;
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
    [IgnoreMember]
    [FieldOffset(0)]
    public byte b0;
    [IgnoreMember]
    [FieldOffset(1)]
    public byte b1;
    [IgnoreMember]
    [FieldOffset(2)]
    public byte b2;
    [IgnoreMember]
    [FieldOffset(3)]
    public byte b3;

    public static implicit operator uint (Difficulty difficulty) => difficulty.Value;

    public override string ToString()
    {
        return Math.Round(BigInteger.Log(this.ToWork(), 2), 4, MidpointRounding.ToZero).ToString(CultureInfo.InvariantCulture);
    }
}
