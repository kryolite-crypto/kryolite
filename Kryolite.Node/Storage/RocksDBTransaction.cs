using Kryolite.RocksDb;

namespace Kryolite.Node.Storage;

internal class RocksDBTransaction : ITransaction
{
    RocksDb.RocksDb Connection { get; }
    RocksDBStorage Store { get; }
    WriteBatch Batch { get; }

    public bool IsDisposed => Disposed;

    private bool Disposed = false;

    public RocksDBTransaction(RocksDb.RocksDb connection, RocksDBStorage store)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Batch = Connection.CreateWriteBatch();
    }

    public void Commit()
    {
        using var opts = new FlushOptions();
        Batch.Put("Key", [], BitConverter.GetBytes(Store.GetCurrentKey()));

        Connection.Write(Batch);
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
