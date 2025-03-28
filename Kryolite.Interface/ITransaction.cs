using Kryolite.RocksDb;

namespace Kryolite.Interface;

public interface ITransaction : IDisposable
{
    void Commit();
    void Rollback();
    WriteBatch GetConnection();
    bool IsDisposed { get; }
}
