using System.Runtime.InteropServices;

namespace Kryolite.RocksDb;

public class ReadOptions : IDisposable
{
    public nint Handle => _handle;

    private nint _handle;
    private nint _iterateLowerBound;
    private nint _iterateUpperBound;

    public ReadOptions()
    {
        _handle = Interop.rocksdb_readoptions_create();
    }

    public ReadOptions CreateIfMissing(bool value)
    {
        Interop.rocksdb_options_set_create_if_missing(_handle, value);
        return this;
    }

    public ReadOptions PrefixSameAsStart(bool value)
    {
        Interop.rocksdb_readoptions_set_prefix_same_as_start(_handle, value);
        return this;
    }

    public ReadOptions IterateLowerBound(byte[] key)
    {
        if (_iterateLowerBound == 0)
        {
            _iterateLowerBound = Marshal.AllocHGlobal(key.Length);
        }
        
        Marshal.Copy(key, 0, _iterateLowerBound, key.Length);
        Interop.rocksdb_readoptions_set_iterate_lower_bound(_handle, _iterateLowerBound, (nuint)key.Length);

        return this;
    }

    public ReadOptions IterateUpperBound(byte[] key)
    {
        if (_iterateUpperBound == 0)
        {
            _iterateUpperBound = Marshal.AllocHGlobal(key.Length);
        }
        
        Marshal.Copy(key, 0, _iterateUpperBound, key.Length);

        Interop.rocksdb_readoptions_set_iterate_upper_bound(_handle, _iterateUpperBound, (nuint)key.Length);
        return this;
    }

    public void Dispose()
    {
        if (_iterateLowerBound != 0)
        {
            Marshal.FreeHGlobal(_iterateLowerBound);
        }

        if (_iterateUpperBound != 0)
        {
            Marshal.FreeHGlobal(_iterateUpperBound);
        }

        Interop.rocksdb_readoptions_destroy(_handle);
    }
}
