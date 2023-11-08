using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kryolite.Shared.Algorithm;

public class GrasshopperStackMachine
{
    public long Checksum { get; private set; }

    private long[] _locals = new long[17];
    private Stack<long> _evaluation = new Stack<long>(8);
    private List<(Op Op, int I32, long I64)> _operations = new (8192 * 2);

    public void Reset()
    {
        Array.Clear(_locals);
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
                        var a = _evaluation.Pop();
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
                    Push(_evaluation.Peek());
                    break;
                case Op.Add:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push(a + b);
                    }
                    break;
                case Op.Sub:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push(a - b);
                    }
                    break;
                case Op.Mul:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push(a * b);
                    }
                    break;
                case Op.And:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push(a & b);
                    }
                    break;
                case Op.Not:
                    {
                        var a = _evaluation.Pop();
                        Push(~a);
                    }
                    break;
                case Op.Or:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push(a | b);
                    }
                    break;
                case Op.Xor:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push(a ^ b);
                    }
                    break;
                case Op.Rotl:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push((long)BitOperations.RotateLeft((ulong)a, (int)b));
                    }
                    break;
                case Op.Rotr:
                    {
                        var a = _evaluation.Pop();
                        var b = _evaluation.Pop();

                        Push((long)BitOperations.RotateRight((ulong)a, (int)b));
                    }
                    break;
                case Op.ShrUn:
                    {
                        var a = (ulong)_evaluation.Pop();
                        var b = (int)_evaluation.Pop();

                        Push((long)(a >> b));
                    }
                    break;
                case Op.Shl:
                    {
                        var a = (ulong)_evaluation.Pop();
                        var b = (int)_evaluation.Pop();

                        Push((long)(a << b));
                    }
                    break;
                case Op.Ret:
                    return _evaluation.Pop();
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

    private void Push(long value)
    {
        Checksum += value;
        _evaluation.Push(value);
    }
}
