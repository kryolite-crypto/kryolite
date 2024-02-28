using System.Runtime.InteropServices;

namespace Kryolite.RocksDb;

public class Iterator : IDisposable
{
    public nint Handle => _handle;
    private nint _handle;

    public Iterator(nint db_handle, nint cf_handle, nint opts_handle)
    {
        _handle = Interop.rocksdb_create_iterator_cf(db_handle, opts_handle, cf_handle);
    }

    public bool Valid()
    {
        return Interop.rocksdb_iter_valid(_handle);
    }

    public void SeekToFirst()
    {
        Interop.rocksdb_iter_seek_to_first(_handle);
    }

    public void SeekToLast()
    {
        Interop.rocksdb_iter_seek_to_last(_handle);
    }

    public unsafe void Seek(ReadOnlySpan<byte> key)
    {
        fixed (byte* ptr = key)
        {
            Interop.rocksdb_iter_seek(_handle, ptr, (nuint)key.Length);
        }
    }

    public unsafe void SeekForPrev(ReadOnlySpan<byte> key)
    {
        fixed (byte* ptr = key)
        {
            Interop.rocksdb_iter_seek_for_prev(_handle, ptr, (nuint)key.Length);
        }
    }

    public void Next()
    {
        Interop.rocksdb_iter_next(_handle);
    }

    public void Prev()
    {
        Interop.rocksdb_iter_prev(_handle);
    }

    public byte[] Key()
    {
        var ptr = Interop.rocksdb_iter_key(_handle, out var len);

        if (ptr == 0)
        {
            return [];
        }

        var result = GC.AllocateUninitializedArray<byte>((int)len, false);

        Marshal.Copy(ptr, result, 0, (int)len);
        Interop.rocksdb_free(ptr);

        return result;
    }

    public byte[] Value()
    {
        var ptr = Interop.rocksdb_iter_value(_handle, out var len);

        if (ptr == 0)
        {
            return [];
        }

        var result = GC.AllocateUninitializedArray<byte>((int)len, false);

        Marshal.Copy(ptr, result, 0, (int)len);

        // ptr belongs to iterator and should not be freed here

        return result;
    }

    public void Dispose()
    {
        Interop.rocksdb_iter_destroy(_handle);
    }
}
