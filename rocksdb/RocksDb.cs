using System.Runtime.InteropServices;

namespace Kryolite.RocksDb;

public partial class RocksDb : IDisposable
{
    private nint _handle;

    private DbPath _path;
    private DbOptions _options;

    private Dictionary<string, ColumnFamily> _columns = new();

    public RocksDb(string path, DbOptions options, List<(string ColumnName, int KeySize, DbOptions ColumnOptions)> columnFamilies)
    {
        _path = new DbPath(path);
        _options = options;

        var c_names = new string[columnFamilies.Count];
        var c_opts = new nint[columnFamilies.Count];
        var c_handles = new nint[columnFamilies.Count];

        for (var i = 0; i < columnFamilies.Count; i++)
        {
            c_names[i] = columnFamilies[i].ColumnName;
            c_opts[i] = columnFamilies[i].ColumnOptions.Handle;
        }

        _handle = Interop.rocksdb_open_column_families(_options.Handle, _path.Handle, columnFamilies.Count, c_names, c_opts, c_handles, out nint errorptr);

        if (errorptr != 0)
        {
            throw new RocksDbException(errorptr);
        }

        for (var i = 0; i < columnFamilies.Count; i++)
        {
            var (name, keySize, opts) = columnFamilies[i];
            var column = new ColumnFamily(c_handles[i], keySize, opts);

            _columns.Add(name, column);
        }
    }

    public unsafe bool HasKey(string columnFamily, ReadOnlySpan<byte> key, ReadOptions opts)
    {
        fixed(byte* keyptr = key)
        {
            var result = Interop.rocksdb_get_cf(_handle, opts.Handle, _columns[columnFamily].Handle, (nint)keyptr, key.Length, out _, out nint errorptr);

            if (errorptr != 0)
            {
                Interop.rocksdb_free(errorptr);
                return false;
            }

            if (result == 0)
            {
                return false;
            }

            Interop.rocksdb_free(result);
            return true;
        }
    }

    public unsafe bool HasKey(string columnFamily, ReadOnlySpan<byte> key)
    {
        using var opts = new ReadOptions();
        return HasKey(columnFamily, key, opts);
    }

    public int KeySize(string columnFamily)
    {
        return _columns[columnFamily].KeySize;
    }

    public unsafe byte[] Get(string columnFamily, ReadOnlySpan<byte> key, ReadOptions opts)
    {
        fixed(byte* keyptr = key)
        {
            var result = Interop.rocksdb_get_cf(_handle, opts.Handle, _columns[columnFamily].Handle, (nint)keyptr, key.Length, out var vallen, out nint errorptr);

            if (errorptr != 0)
            {
                throw new RocksDbException(errorptr);
            }

            if (result == 0)
            {
                return [];
            }

            var bytes = GC.AllocateUninitializedArray<byte>((int)vallen, false);

            Marshal.Copy(result, bytes, 0, (int)vallen);
            Interop.rocksdb_free(result);

            return bytes;
        }
    }

    public unsafe byte[] Get(string columnFamily, ReadOnlySpan<byte> key)
    {
        using var opts = new ReadOptions();
        return Get(columnFamily, key, opts);
    }

    public unsafe byte[][] GetMany(string columnFamily, byte[][] keys, ReadOptions opts)
    {
        var cfs = GC.AllocateUninitializedArray<nint>(keys.Length, false);
        var handles = GC.AllocateUninitializedArray<GCHandle>(keys.Length, false);
        var pinned = GC.AllocateUninitializedArray<nint>(keys.Length, false);
        var keylens = GC.AllocateUninitializedArray<int>(keys.Length, false);
        var values = GC.AllocateUninitializedArray<nint>(keys.Length, false);
        var valuelens = GC.AllocateUninitializedArray<int>(keys.Length, false);
        var errors = GC.AllocateUninitializedArray<nint>(keys.Length, false);

        Array.Fill(cfs, _columns[columnFamily].Handle);

        for (var i = 0; i < keys.Length; i++)
        {
            var handle = GCHandle.Alloc(keys[i], GCHandleType.Pinned);
            
            handles[i] = handle;
            pinned[i] = handle.AddrOfPinnedObject();
            keylens[i] = keys[i].Length;
        }

        Interop.rocksdb_multi_get_cf(_handle, opts.Handle, cfs, keys.Length, pinned, keylens, values, valuelens, errors);

        for (var i = 0; i < keys.Length; i++)
        {
            handles[i].Free();
        }

        var results = GC.AllocateUninitializedArray<byte[]>(values.Length, false);

        for (var i = 0; i < values.Length; i++)
        {
            var valuelen = valuelens[i];
            var valueptr = values[i];
            var errorptr = errors[i];

            if (errorptr != 0 || valueptr == 0)
            {
                if (errorptr != 0)
                {
                    Interop.rocksdb_free(errorptr);
                }

                if (valueptr != 0)
                {
                    Interop.rocksdb_free(valueptr);
                }

                results[i] = [];
                continue;
            }

            var value = GC.AllocateUninitializedArray<byte>(valuelen, false);

            Marshal.Copy(valueptr, value, 0, valuelen);

            Interop.rocksdb_free(valueptr);

            results[i] = value;
        }

        return results;
    }

    public unsafe byte[][] GetMany(string columnFamily, byte[][] keys)
    {
        using var opts = new ReadOptions();
        return GetMany(columnFamily, keys, opts);
    }

    public unsafe void Put(string columnFamily, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WriteOptions opts)
    {
        fixed(byte* keyptr = key, valptr = value)
        {
            Interop.rocksdb_put_cf(_handle, opts.Handle, _columns[columnFamily].Handle, (nint)keyptr, key.Length, (nint)valptr, value.Length, out nint errorptr);

            if (errorptr != 0)
            {
                throw new RocksDbException(errorptr);
            }
        }
    }

    public void Put(string columnFamily, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        using var opts = new WriteOptions();
        Put(columnFamily, key, value, opts);
    }

    public unsafe void Delete(string columnFamily, ReadOnlySpan<byte> key, WriteOptions opts)
    {
        fixed(byte* keyptr = key)
        {
            Interop.rocksdb_delete_cf(_handle, opts.Handle, _columns[columnFamily].Handle, (nint)keyptr, key.Length, out var errorptr);

            if (errorptr != 0)
            {
                throw new RocksDbException(errorptr);
            }
        }
    }

    public void Delete(string columnFamily, ReadOnlySpan<byte> key)
    {
        using var opts = new WriteOptions();
        Delete(columnFamily, key);
    }

    public WriteBatch CreateWriteBatch()
    {
        return new WriteBatch(_columns);
    }

    public Iterator CreateIterator(string columnFamily, ReadOptions opts)
    {
        return new Iterator(_handle, _columns[columnFamily].Handle, opts.Handle);
    }

    public void RecreateColumnFamily(string columnFamily)
    {
        if (!_columns.Remove(columnFamily, out var cf))
        {
            return;
        }

        Interop.rocksdb_drop_column_family(_handle, cf.Handle, out nint errorptr);

        if (errorptr != 0)
        {
            throw new RocksDbException(errorptr);
        }

        var keySize = cf.KeySize;
        var opts = cf.Options;

        cf.Dispose();

        var handle = Interop.rocksdb_create_column_family(_handle, opts.Handle, columnFamily, out errorptr);

        if (errorptr != 0)
        {
            throw new RocksDbException(errorptr);
        }

        _columns.Add(columnFamily, new ColumnFamily(handle, keySize, opts));
    }

    public Checkpoint Checkpoint()
    {
        var handle = Interop.rocksdb_checkpoint_object_create(_handle);
        return new Checkpoint(handle);
    }

    public void Write(WriteBatch writeBatch, WriteOptions opts)
    {
        Interop.rocksdb_write(_handle, opts.Handle, writeBatch.Handle, out nint errorptr);

        if (errorptr != 0)
        {
            throw new RocksDbException(errorptr);
        }
    }

    public void Write(WriteBatch writeBatch)
    {
        using var opts = new WriteOptions();
        Write(writeBatch, opts);
    }

    public void Flush(FlushOptions opts)
    {
        Interop.rocksdb_flush(_handle, opts.Handle);
    }

    public void Dispose()
    {
        foreach (var col in _columns)
        {
            col.Value.Dispose();
        }

        Interop.rocksdb_close(_handle);

        _path.Dispose();
        _options.Dispose();
    }
}
