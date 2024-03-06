using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Kryolite.Shared;

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
