using System.Buffers.Binary;
using Kryolite.Model;
using Kryolite.Type;
using Wasmtime;

namespace Kryolite.Module.SmartContract;

public static class Extensions
{
    public static void CopyTo(this uint value, Span<byte> target)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target, value);
    }

    public static void CopyTo(this long value, Span<byte> target)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target, value);
    }

    public static void CopyTo(this ulong value, Span<byte> target)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(target, value);
    }

    public static void CopyTo(this TransactionType value, Span<byte> target)
    {
        target[0] = (byte)value;
    }

    public static Span<byte> SliceAndIncrement(this Span<byte> span, ref int pos, int length)
    {
        var slice = span.Slice(pos, length);
        pos += length;
        return slice;
    }

    public static IContext GetContext(this Caller caller)
    {
        if (caller.GetData() is not IContext context)
        {
            throw new Exception("Invalid store");
        }

        return context;
    }

    public static Memory GetMemory(this Caller caller)
    {
        var memory = caller.GetMemory();

        if (memory is null)
        {
            throw new Exception("Invalid memory");
        }

        return memory;
    }

    public static Address ReadAddress(this Memory memory, int ptr)
    {
        return (Address)memory.GetSpan(ptr, Address.ADDRESS_SZ);
    }

    public static SHA256Hash ReadU256(this Memory memory, int ptr)
    {
        return (SHA256Hash)memory.GetSpan(ptr, SHA256Hash.HASH_SZ);
    }
}
