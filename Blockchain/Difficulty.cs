using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Marccacoin;

[StructLayout(LayoutKind.Explicit)]
public record struct Difficulty
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
}