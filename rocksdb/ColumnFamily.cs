namespace Kryolite.RocksDb;

public class ColumnFamily : IDisposable
{
    public nint Handle => _handle;
    public int KeySize => _keySize;
    public DbOptions Options => _options;

    private nint _handle;
    private int _keySize;
    private DbOptions _options;

    public ColumnFamily(nint handle, int keySize, DbOptions options)
    {
        _handle = handle;
        _keySize = keySize;
        _options = options;
    }

    public void Dispose()
    {
        Interop.rocksdb_column_family_handle_destroy(_handle);
    }
}
