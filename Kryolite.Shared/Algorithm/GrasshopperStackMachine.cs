using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kryolite.Shared.Algorithm;

public unsafe ref struct GrasshopperStackMachine
{
    public long Checksum;

    private long* _locals;
    private long* _values;

    private long* _stackStart;
    private long* _stackPtr;

    private byte* _operationsStart;
    private byte* _opsPtr;

    public GrasshopperStackMachine()
    {
        _stackStart = (long*)Marshal.AllocHGlobal(sizeof(long) * 256);
        _stackPtr = _stackStart;

        _operationsStart = (byte*)Marshal.AllocHGlobal(2048);
        _opsPtr = _operationsStart;

        _values = (long*)Marshal.AllocHGlobal(sizeof(long) * 2048);
        _locals = (long*)Marshal.AllocHGlobal(sizeof(long) * 17);
    }

    public void Reset()
    {
        _stackPtr = _stackStart;
        _opsPtr = _operationsStart;
    }

    public void Emit(Op op)
    {
        *_opsPtr++ = (byte)op;
        Checksum += (byte)op;
    }

    public void Emit(Op op, int addr)
    {
        _values[_opsPtr - _operationsStart] = addr;
        *_opsPtr++ = (byte)op;
        Checksum += (byte)op;
    }

    public void Emit(Op op, long addr)
    {
        _values[_opsPtr - _operationsStart] = addr;
        *_opsPtr++ = (byte)op;
        Checksum += (byte)op;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long Invoke()
    {
        var ptr = _operationsStart;

        while (ptr < _opsPtr)
        {
            var op = *ptr;

            switch ((Op)op)
            {
                case Op.LdLoc:
                    Push(_locals[_values[ptr - _operationsStart]]);
                    break;
                case Op.StLoc:
                    _locals[_values[ptr - _operationsStart]] = Pop();
                    break;
                case Op.LdInt32:
                    Push(_values[ptr - _operationsStart]);
                    break;
                case Op.LdInt64:
                    Push(_values[ptr - _operationsStart]);
                    break;
                case Op.Dup:
                    Push(Peek());
                    break;
                case Op.Add:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a + b);
                    }
                    break;
                case Op.Sub:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a - b);
                    }
                    break;
                case Op.Mul:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a * b);
                    }
                    break;
                case Op.And:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a & b);
                    }
                    break;
                case Op.Not:
                    {
                        var a = Pop();
                        Push(~a);
                    }
                    break;
                case Op.Or:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a | b);
                    }
                    break;
                case Op.Xor:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a ^ b);
                    }
                    break;
                case Op.Rotl:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push((long)BitOperations.RotateLeft((ulong)a, (int)b));
                    }
                    break;
                case Op.Rotr:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push((long)BitOperations.RotateRight((ulong)a, (int)b));
                    }
                    break;
                case Op.ShrUn:
                    {
                        var b = (int)Pop();
                        var a = Pop();

                        Push(a >> b);
                    }
                    break;
                case Op.Shl:
                    {
                        var b = (int)Pop();
                        var a = Pop();

                        Push(a << b);
                    }
                    break;
                case Op.SHA256:
                    {
                        var b = (int)Pop();
                        var a = Pop();
                        var span = new Span<byte>(_locals, 8 * 16);

                        SHA256.TryHashData(span, span[b..32], out _);

                        for (var x = b / 8; x < 16; x++)
                        {
                            a += _locals[x];
                        }

                        Push(a);
                    }
                    break;
                case Op.PopCnt:
                    {
                        var a = Pop();
                        Push(a + BitOperations.PopCount((ulong)a));
                    }
                    break;
                case Op.LZCnt:
                    {
                        var a = Pop();
                        Push(a + BitOperations.LeadingZeroCount((ulong)a));
                    }
                    break;
                case Op.TZCnt:
                    {
                        var a = Pop();
                        Push(a + BitOperations.TrailingZeroCount((ulong)a));
                    }
                    break;
                case Op.CRC32C:
                    {
                        var a = Pop();
                        var crc = (uint)(a % uint.MaxValue);
                        
                        for (var x = 0; x < 16; x++)
                        {
                            crc += BitOperations.Crc32C(crc, (ulong)_locals[x]);
                        }

                        Push(a + crc);
                    }
                    break;
                case Op.Cmp:
                    {
                        var b = Pop();
                        var a = Pop();

                        Push(a - b);
                    }
                    break;
                case Op.Jmp:
                    {
                        ptr += _values[ptr - _operationsStart];
                    }
                    break;
                case Op.JmpIfZero:
                    {
                        var a = Pop();

                        if (a == 0)
                        {
                            ptr += _values[ptr - _operationsStart];
                        }
                    }
                    break;
                case Op.Ret:
                    return Pop();
            }

            ptr++;
        }

        throw new Exception("invalid stack");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(long value)
    {
        Checksum += value;
        *_stackPtr++ = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long Pop()
    {
        return *--_stackPtr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly long Peek()
    {
        return *(_stackPtr - 1);
    }

    public readonly void Dispose()
    {
        Marshal.FreeHGlobal((nint)_stackStart);
        Marshal.FreeHGlobal((nint)_locals);
        Marshal.FreeHGlobal((nint)_operationsStart);
        Marshal.FreeHGlobal((nint)_values);
    }
}
