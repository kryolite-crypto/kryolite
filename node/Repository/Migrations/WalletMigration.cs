using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Migrations;

public class WalletMigration
{
    public SQLiteConnection Connection { get; }

    public WalletMigration(SQLiteConnection connection)
    {
        Connection = connection;
    }

    public void Baseline()
    {
        using var cmd = Connection.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE Wallet (
                Address TEXT PRIMARY KEY,
                Description TEXT,
                PublicKey TEXT,
                PrivateKey BLOB,
                WalletType INTEGER
            );
        ";

        cmd.ExecuteNonQuery();
    }
}
