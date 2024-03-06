using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kryolite.Shared;

public ref partial struct Serializer
{
    public static int SizeOf(byte[] value)
    {
        return value.Length;
    }

    public static int SizeOf(string[]? value)
    {
        var length = sizeof(int);

        if (value is null)
        {
            return length;
        }
        
        for (var i = 0; i < value.Length; i++)
        {
            length += SizeOf(value[i]);
        }

        return length;
    }

    public static int SizeOf<T>(T value) where T : unmanaged
    {
        return Unsafe.SizeOf<T>();
    }

    public static int SizeOf(string? value)
    {
        if (value is null)
        {
            return sizeof(int);
        }

        return sizeof(int) + Encoding.UTF8.GetByteCount(value);
    }

    public static int SizeOf(BigInteger value)
    {
        return sizeof(int) + value.GetByteCount();
    }

    public static int SizeOf(ISerializable value)
    {
        return value.GetLength();
    }

    public static int SizeOfN(ISerializable? value)
    {
        if (value is null)
        {
            return sizeof(bool);
        }

        return sizeof(bool) + value.GetLength();
    }

    public static int SizeOf<T>(List<T> values) where T : ISerializable
    {
        var size = sizeof(int);

        for (var i = 0; i < values.Count; i++)
        {
            size += values[i].GetLength();
        }

        return size;
    }
}