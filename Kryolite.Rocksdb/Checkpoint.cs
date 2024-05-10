namespace Kryolite.RocksDb;

public class Checkpoint : IDisposable
{
    private nint _handle;

    public Checkpoint(nint handle)
    {
        _handle = handle;
    }

    public void Save(string path)
    {
        Interop.rocksdb_checkpoint_create(_handle, path, 1024 * 1024, out nint errorptr);

        if (errorptr != 0)
        {
            throw new RocksDbException(errorptr);
        }
    }

    public void Dispose()
    {
        Interop.rocksdb_checkpoint_object_destroy(_handle);
    }
}
