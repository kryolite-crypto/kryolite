namespace Kryolite.RocksDb;

public class WriteOptions : IDisposable
{
    public nint Handle => _handle;

    private nint _handle;

    public WriteOptions()
    {
        _handle = Interop.rocksdb_writeoptions_create();
    }

    public WriteOptions CreateIfMissing(bool value)
    {
        Interop.rocksdb_options_set_create_if_missing(_handle, value);
        return this;
    }

    public void Dispose()
    {
        Interop.rocksdb_writeoptions_destroy(_handle);
    }
}
