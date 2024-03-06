using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kryolite.Shared;

public ref partial struct Serializer(ref byte spanRef, int length)
{
    ref byte _spanRef = ref spanRef;
    ref byte _end = ref Unsafe.Add(ref spanRef, length);

    private static Func<SerializerEnum, ISerializable> _resolver = (e) => null!;

    public static byte[] Serialize<T>(T instance) where T : ISerializable
    {
        var length = instance.GetLength();
        var array = new byte[length + 1];

        array[0] = instance.GetSerializerId();

        ref var spanRef = ref MemoryMarshal.GetReference(array.AsSpan()[1..]);
        var serializer = new Serializer(ref spanRef, length);

        instance.Serialize(ref serializer);
        return array;
    }

    public static void Serialize<T>(T instance, IBufferWriter<byte> writer) where T : ISerializable
    {
        var length = 1 + instance.GetLength();
        var span = writer.GetSpan(length);

        span[0] = instance.GetSerializerId();

        ref var spanRef = ref MemoryMarshal.GetReference(span[1..]);
        var serializer = new Serializer(ref spanRef, length);

        instance.Serialize(ref serializer);

        writer.Advance(length);
    }

    public static void Serialize(long value, IBufferWriter<byte> writer)
    {
        var length = sizeof(long);
        var span = writer.GetSpan(length);

        BitConverter.TryWriteBytes(span, value);

        writer.Advance(length);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : ISerializable, new()
    {
        var instance = new T();

        if (bytes[0] != instance.GetSerializerId())
        {
            ThrowInvalidHeader();
        }

        ref var spanRef = ref MemoryMarshal.GetReference(bytes[1..]);
        var serializer = new Serializer(ref spanRef, bytes.Length);

        instance.Deserialize(ref serializer);
        return instance;
    }

    public static T Deserialize<T>(ReadOnlySequence<byte> sequence) where T : ISerializable, new()
    {
        var span = sequence.FirstSpan;
        var instance = new T();

        if (span[0] != instance.GetSerializerId())
        {
            ThrowInvalidHeader();
        }

        ref var spanRef = ref MemoryMarshal.GetReference(span[1..]);
        var serializer = new Serializer(ref spanRef, span.Length);

        instance.Deserialize(ref serializer);

        return (T)instance;
    }

    [DoesNotReturn]
    private static void ThrowOutOfRange()
    {
        throw new ArgumentOutOfRangeException("not enought bytes");
    }

    [DoesNotReturn]
    private static void ThrowInvalidHeader()
    {
        throw new Exception("invalid header received");
    }
}