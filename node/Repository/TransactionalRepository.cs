using LiteDB;

namespace Kryolite.Node;


public abstract class TransactionalRepository : IDisposable
{
    protected LiteDatabase Database;
    protected readonly bool Transactional;
    protected bool Committed;

    public TransactionalRepository(string connectionString, bool transactional = false)
    {
        Database = new LiteDatabase($"Filename={BlockchainService.DATA_DIR}{connectionString}");

        Directory.CreateDirectory(BlockchainService.DATA_DIR);

        if (transactional) {
            Database.BeginTrans();
        }
    }

    public void Checkpoint()
    {
        Database.Checkpoint();
    }

    public void Commit()
    {
        Database.Commit();
        Committed = true;
    }

    public void Dispose()
    {
        if (!Committed) {
            Database.Rollback();
        }

        Database.Dispose();
    }
}
