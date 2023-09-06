using RocksDbSharp;
using System.Reflection;

namespace Kryolite.Node.Storage;

public class RocksDbFlushOptions : FlushOptions
{
    public RocksDbFlushOptions()
    {
        typeof(DbOptions)?.GetField("Handle", BindingFlags.NonPublic | BindingFlags.SetField | BindingFlags.Instance)?
            .SetValue(this, Native.Instance.rocksdb_flushoptions_create());

        base.SetWaitForFlush(true);
    }
}
