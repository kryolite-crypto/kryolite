using RocksDbSharp;
using System.Collections.Concurrent;

namespace Kryolite.Node.Storage;

internal class RocksDBTransaction : ITransaction
{
    RocksDb Connection { get; }
    RocksDBStorage Store { get; }
    WriteBatch Batch { get; }

    public bool IsDisposed => Disposed;

    private bool Disposed = false;

    private static FlushOptions opts = new RocksDbFlushOptions();

    public RocksDBTransaction(RocksDb connection, RocksDBStorage store)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Batch = new WriteBatch();
    }

    public void Commit()
    {
        var keyColumn = Connection.GetColumnFamily("Key");
        var key = new byte[1];

        Batch.Put(key, BitConverter.GetBytes(Store.GetCurrentKey()), keyColumn);

        var wOpts = new WriteOptions();
        wOpts.SetSync(true);

        Connection.Write(Batch, wOpts);
        Connection.Flush(opts);
    }

    public void Rollback()
    {
        Batch.Clear();
    }

    public void Dispose()
    {
        Disposed = true;
        Batch.Dispose();
    }

    public WriteBatch GetConnection()
    {
        return Batch;
    }
}
