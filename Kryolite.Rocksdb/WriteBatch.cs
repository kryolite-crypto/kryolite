namespace Kryolite.RocksDb;

public class WriteBatch : IDisposable
{
    public nint Handle => _handle;

    private nint _handle;
    private Dictionary<string, ColumnFamily> _columns;

    public WriteBatch(Dictionary<string, ColumnFamily> columns)
    {
        _handle = Interop.rocksdb_writebatch_create();
        _columns = columns;
    }

    public unsafe void Put(string columnFamily, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        fixed(byte* keyptr = key, valptr = value)
        {
            Interop.rocksdb_writebatch_put_cf(_handle, _columns[columnFamily].Handle, (nint)keyptr, key.Length, (nint)valptr, value.Length);
        }
    }

    public unsafe void Delete(string columnFamily, ReadOnlySpan<byte> key)
    {
        fixed(byte* keyptr = key)
        {
            Interop.rocksdb_writebatch_delete_cf(_handle, _columns[columnFamily].Handle, (nint)keyptr, key.Length);
        }
    }

    public void Clear()
    {
        Interop.rocksdb_writebatch_clear(_handle);
    }

    public void Dispose()
    {
        Interop.rocksdb_writebatch_destroy(_handle);
    }
}
