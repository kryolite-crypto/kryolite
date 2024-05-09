namespace Kryolite.RocksDb;

public class DbOptions : IDisposable
{
    public nint Handle => _handle;

    private nint _handle;

    public DbOptions()
    {
        _handle = Interop.rocksdb_options_create();
    }

    public DbOptions CreateIfMissing(bool value)
    {
        Interop.rocksdb_options_set_create_if_missing(_handle, value);
        return this;
    }

    public DbOptions WalDir(string path)
    {
        using var dbPath = new DbPath(path);
        Interop.rocksdb_options_set_wal_dir(_handle, dbPath.Handle);
        return this;
    }

    public DbOptions KeepLogFileNum(int num)
    {
        Interop.rocksdb_options_set_keep_log_file_num(_handle, (nuint)num);
        return this;
    }

    public DbOptions CreateMissingColumnFamilies(bool value)
    {
        Interop.rocksdb_options_set_create_missing_column_families(_handle, value);
        return this;
    }

    public DbOptions SetDbWriteBufferSize(uint value)
    {
        Interop.rocksdb_options_set_db_write_buffer_size(_handle, value);
        return this;
    }

    public DbOptions SetWriteBufferSize(uint value)
    {
        Interop.rocksdb_options_set_write_buffer_size(_handle, value);
        return this;
    }

    public void Dispose()
    {
        Interop.rocksdb_options_destroy(_handle);
    }
}