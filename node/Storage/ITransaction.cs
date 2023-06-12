using RocksDbSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Storage;

public interface ITransaction : IDisposable
{
    void Commit();
    void Rollback();
    WriteBatchWithIndex GetConnection();
}
