using LiteDB;

namespace Marccacoin;


public abstract class TransactionalRepository : IDisposable
{
    protected LiteDatabase Database;
    protected readonly bool Transactional;
    protected bool Committed;

    public TransactionalRepository(string connectionString, bool transactional = false)
    {
        Database = new LiteDatabase(connectionString);

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
