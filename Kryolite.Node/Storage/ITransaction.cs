using Kryolite.RocksDb;

namespace Kryolite.Node.Storage;

public interface ITransaction : IDisposable
{
    void Commit();
    void Rollback();
    WriteBatch GetConnection();
    bool IsDisposed { get; }
}
