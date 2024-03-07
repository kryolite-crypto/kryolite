using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Kryolite.Shared;

public ref partial struct Serializer
{
    public void Read(ref byte value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(byte));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        value = Unsafe.ReadUnaligned<byte>(ref _spanRef);
        _spanRef = ref next;
    }

    public void Read(ref int value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(int));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        value = Unsafe.ReadUnaligned<int>(ref _spanRef);
        _spanRef = ref next;
    }

    public void Read(ref uint value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(uint));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        value = Unsafe.ReadUnaligned<uint>(ref _spanRef);
        _spanRef = ref next;
    }

    public void Read(ref long value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(long));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        value = Unsafe.ReadUnaligned<long>(ref _spanRef);
        _spanRef = ref next;
    }

    public void Read(ref ulong value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(ulong));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        value = Unsafe.ReadUnaligned<ulong>(ref _spanRef);
        _spanRef = ref next;
    }

    public void Read(ref bool value)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, sizeof(bool));

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        value = Unsafe.ReadUnaligned<bool>(ref _spanRef);
        _spanRef = ref next;
    }

    public void Read(ref BigInteger target)
    {
        var dataLen = 0;

        Read(ref dataLen);

        if (dataLen == 0)
        {
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, dataLen);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        var span = MemoryMarshal.CreateReadOnlySpan(ref _spanRef, dataLen);
        target = new BigInteger(span);

        _spanRef = ref next;
    }

    public void Read(ref string target)
    {
        var dataLen = 0;

        Read(ref dataLen);

        if (dataLen == 0)
        {
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, dataLen);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        var span = MemoryMarshal.CreateReadOnlySpan(ref _spanRef, dataLen);
        target = Encoding.UTF8.GetString(span);

        _spanRef = ref next;
    }

    public void ReadN(ref string? target)
    {
        var dataLen = 0;

        Read(ref dataLen);

        if (dataLen == -1)
        {
            target = null;
            return;
        }

        if (dataLen == 0)
        {
            target = string.Empty;
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, dataLen);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        var span = MemoryMarshal.CreateReadOnlySpan(ref _spanRef, dataLen);
        target = Encoding.UTF8.GetString(span);

        _spanRef = ref next;
    }

    public void Read(ref DateTimeOffset source)
    {
        var timestamp = 0L;
        Read(ref timestamp);
        source = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }

    public void Read(ref byte[] target)
    {
        var dataLen = 0;

        Read(ref dataLen);

        if (dataLen == 0)
        {
            return;
        }

        ref var next = ref Unsafe.Add(ref _spanRef, dataLen);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        target = new byte[dataLen];
        Unsafe.CopyBlockUnaligned(ref MemoryMarshal.GetReference<byte>(target), ref _spanRef, (uint)dataLen);
        _spanRef = ref next;
    }

    public void Read(ref string[]? target)
    {
        var dataLen = 0;

        Read(ref dataLen);

        if (dataLen == -1)
        {
            target = null;
            return;
        }

        target = new string[dataLen];

        for (var i = 0; i < dataLen; i++)
        {
            string str = string.Empty;
            Read(ref str);
            target[i] = str;
        }
    }

    public void Read<T>(ref T value) where T : ISerializable, new()
    {
        value.Deserialize(ref this);
    }

    public void ReadN<T>(ref T? value) where T : ISerializable?, new()
    {
        var isnull = false;

        Read(ref isnull);

        if (isnull)
        {
            return;
        }

        value = new T();
        value.Deserialize(ref this);
    }

    public void Read(ref byte[] target, uint length)
    {
        ref var next = ref Unsafe.Add(ref _spanRef, length);

        AddressOutOfRangeException.ThrowIfAddressIsGreaterThan(ref next, ref _end);

        Unsafe.CopyBlockUnaligned(ref MemoryMarshal.GetReference<byte>(target), ref _spanRef, length);
        _spanRef = ref next;
    }

    public void Read<T>(ref List<T> target) where T : ISerializable, new()
    {
        var dataCount = 0;
    
        Read(ref dataCount);

        if (dataCount == 0)
        {
            return;
        }

        target.EnsureCapacity(dataCount);

        for (var i = 0; i < dataCount; i++)
        {
            var item = new T();
            item.Deserialize(ref this);
            target.Add(item);
        }
    }

    public void Read<T>(ref List<T> target, Func<T> _factory) where T : ISerializable
    {
        var dataCount = 0;
    
        Read(ref dataCount);

        if (dataCount == 0)
        {
            return;
        }

        target.EnsureCapacity(dataCount);

        for (var i = 0; i < dataCount; i++)
        {
            var item = _factory();
            item.Deserialize(ref this);
            target.Add(item);
        }
    }
}
