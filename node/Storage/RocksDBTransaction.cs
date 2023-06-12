using RocksDbSharp;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Kryolite.Node.Storage;

internal class RocksDBTransaction : ITransaction
{
    RocksDb Connection { get; }
    RocksDBStorage Store { get; }
    WriteBatchWithIndex Batch { get; }

    public RocksDBTransaction(RocksDb connection, RocksDBStorage store)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Batch = new WriteBatchWithIndex();
    }

    public void Commit()
    {
        var opts = new FlushOptions();
        opts.SetWaitForFlush(false);

        var keyColumn = Connection.GetColumnFamily("Key");
        var key = new byte[1];

        Batch.Put(key, BitConverter.GetBytes(Store.GetCurrentKey()), keyColumn);

        Connection.Write(Batch);
        Connection.Flush(opts);
    }

    public void Rollback()
    {
        Batch.Clear();
    }

    public void Dispose()
    {
        Batch.Dispose();
    }

    public WriteBatchWithIndex GetConnection()
    {
        return Batch;
    }
}
