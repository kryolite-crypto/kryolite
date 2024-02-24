using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using MemoryPack;

namespace Kryolite.Shared;

[DataContract]
[MemoryPackable]
[StructLayout(LayoutKind.Explicit)]
public partial record struct Difficulty
{
    [FieldOffset(0)]
    public uint Value;
    [MemoryPackIgnore]
    [FieldOffset(0)]
    public byte b0;
    [MemoryPackIgnore]
    [FieldOffset(1)]
    public byte b1;
    [MemoryPackIgnore]
    [FieldOffset(2)]
    public byte b2;
    [MemoryPackIgnore]
    [FieldOffset(3)]
    public byte b3;

    public static implicit operator uint (Difficulty difficulty) => difficulty.Value;

    public override string ToString()
    {
        return Math.Round(BigInteger.Log(this.ToWork(), 2), 4, MidpointRounding.ToZero).ToString(CultureInfo.InvariantCulture);
    }
}
