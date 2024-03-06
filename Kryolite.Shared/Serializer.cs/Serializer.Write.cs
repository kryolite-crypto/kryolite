using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kryolite.Shared;

public ref partial struct Serializer
{

    public void Write(byte value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(byte));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.WriteUnaligned(ref _spanRef, value);
        _spanRef = ref next;
    }

    public void Write(int value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(int));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.WriteUnaligned(ref _spanRef, value);
        _spanRef = ref next;
    }

    public void Write(uint value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(uint));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.WriteUnaligned(ref _spanRef, value);
        _spanRef = ref next;
    }

    public void Write(long value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(long));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.WriteUnaligned(ref _spanRef, value);
        _spanRef = ref next;
    }

    public void Write(ulong value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(ulong));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.WriteUnaligned(ref _spanRef, value);
        _spanRef = ref next;
    }

    public void Write(bool value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(bool));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.WriteUnaligned(ref _spanRef, value);
        _spanRef = ref next;
    }

    public void WriteN<T>(T? value) where T : ISerializable
    {
        var isnull = value is null;

        Write(isnull);

        if (isnull)
        {
            return;
        }

        value!.Serialize(ref this);
    }

    public void Write<T>(T value) where T : ISerializable
    {
        value.Serialize(ref this);
    }

    public void Write<T>(List<T> source) where T : ISerializable
    {
        Write(source.Count);

        if (source.Count == 0)
        {
            return;
        }

        for (var i = 0; i < source.Count; i++)
        {
            source[i].Serialize(ref this);
        }
    }

    public void Write(DateTimeOffset source)
    {
        Write(source.ToUnixTimeMilliseconds());
    }

    public void Write(BigInteger source)
    {
        var length = source.GetByteCount();

        Write(length);

        if (length == 0)
        {
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, length);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Span<byte> span = stackalloc byte[length];
        source.TryWriteBytes(span, out _);

        Unsafe.CopyBlockUnaligned(ref _spanRef, ref MemoryMarshal.GetReference(span), (uint)length);
        _spanRef = ref next;
    }

    public void Write(string? source)
    {
        var length = source is null ?
            -1 : Encoding.UTF8.GetByteCount(source);

        Write(length);

        if (length <= 0)
        {
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, length);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Span<byte> span = stackalloc byte[length];
        Encoding.UTF8.TryGetBytes(source, span, out _);

        Unsafe.CopyBlockUnaligned(ref _spanRef, ref MemoryMarshal.GetReference(span), (uint)length);
        _spanRef = ref next;
    }

    public void Write(byte[] source)
    {
        Write(source.Length);

        if (source.Length == 0)
        {
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, source.Length);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.CopyBlockUnaligned(ref _spanRef, ref MemoryMarshal.GetReference<byte>(source), (uint)source.Length);
        _spanRef = ref next;
    }

    public void Write(string[]? source)
    {
        var length = source is null ? - 1 : source.Length;

        Write(length);

        if (length <= 0)
        {
            return;
        }

        for (var i = 0; i < source?.Length; i++)
        {
            Write(source[i]);
        }
    }

    public void Write(byte[] source, uint length)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, length);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.CopyBlockUnaligned(ref _spanRef, ref MemoryMarshal.GetReference<byte>(source), length);
        _spanRef = ref next;
    }
}