namespace Kryolite.RocksDb;

public class FlushOptions : IDisposable
{
    public nint Handle => _handle;

    private nint _handle;

    public FlushOptions()
    {
        _handle = Interop.rocksdb_flushoptions_create();
    }

    public void WaitForFlush(bool waitForFlush)
    {
        Interop.rocksdb_flushoptions_set_wait(_handle, waitForFlush);
    }

    public void Dispose()
    {
        Interop.rocksdb_flushoptions_destroy(_handle);
    }
}
