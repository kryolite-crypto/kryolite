using System.Numerics;
using System.Runtime.CompilerServices;

namespace Kryolite.Shared.Algorithm;

public unsafe class GrasshopperStackMachine
{
    public long Checksum { get; private set; }

    private long[] _locals = new long[17];
    private long[] _stack = new long[16];
    private int _stackPtr = 0;
    private List<(Op Op, int I32, long I64)> _operations = new (8192 * 2);

    public void Reset()
    {
        _stackPtr = 0;
        _operations.Clear();
    }

    public void Emit(Op op)
    {
        _operations.Add((op, 0, 0));
        Checksum += _operations.Count;
    }

    public void Emit(Op op, int addr)
    {
        _operations.Add((op, addr, 0));
        Checksum += _operations.Count;
    }

    public void Emit(Op op, long addr)
    {
        _operations.Add((op, 0, addr));
        Checksum += _operations.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long Invoke()
    {
        for (var i = 0; i < _operations.Count; i++)
        {
            var ins = _operations[i];

            switch (ins.Op)
            {
                case Op.LdLoc:
                    Push(_locals[ins.I32]);
                    break;
                case Op.StLoc:
                    {
                        var a = Pop();
                        _locals[ins.I32] = a;

                        Checksum += a;
                    }
                    break;
                case Op.LdInt32:
                    Push(ins.I32);
                    break;
                case Op.LdInt64:
                    Push(ins.I64);
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
                        var a = (ulong)Pop();

                        Push((long)(a >> b));
                    }
                    break;
                case Op.Shl:
                    {
                        var b = (int)Pop();
                        var a = (ulong)Pop();

                        Push((long)(a << b));
                    }
                    break;
                case Op.Ret:
                    return Pop();
            }
        }

        throw new Exception("invalid stack");
    }

    public void Print()
    {
        for (var i = 0; i < _operations.Count; i++)
        {
            var ins = _operations[i];
            var bytes = BitConverter.GetBytes(i);
            Array.Reverse(bytes);

            switch (ins.Op)
            {
                case Op.LdLoc:
                case Op.StLoc:
                case Op.LdInt32:
                    Console.WriteLine($"{BitConverter.ToString(bytes).Replace("-", "")}: {ins.Op}.{ins.I32}");
                    break;
                case Op.LdInt64:
                    Console.WriteLine($"{BitConverter.ToString(bytes).Replace("-", "")}: {ins.Op}.{ins.I64}");
                    break;
                default:
                    Console.WriteLine($"{BitConverter.ToString(bytes).Replace("-", "")}: {ins.Op}");
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(long value)
    {
        Checksum += value;
        _stack[_stackPtr++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long Pop()
    {
        return _stack[--_stackPtr];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long Peek()
    {
        return _stack[_stackPtr - 1];
    }
}
