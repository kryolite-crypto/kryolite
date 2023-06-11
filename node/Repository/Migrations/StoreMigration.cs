using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Migrations;

public class StoreMigration
{
    public SqliteConnection Connection { get; }

    public StoreMigration(SqliteConnection connection)
    {
        Connection = connection;
    }

    public void Baseline()
    {
        using var cmd = Connection.CreateCommand();

        cmd.CommandText = @"
                CREATE TABLE Transactions (
                    TransactionId TEXT PRIMARY KEY,
                    TransactionType INTEGER,
                    Height INTEGER,
                    PublicKey TEXT,
                    Sender TEXT,
                    Recipient TEXT,
                    Value INTEGER,
                    Pow TEXT,
                    Data BLOB,
                    Timestamp INTEGER,
                    Signature TEXT,
                    ExecutionResult INTEGER
                ) WITHOUT ROWID;

                CREATE TABLE Ledger (
                    Address TEXT PRIMARY KEY,
                    Balance BIGINT,
                    Pending BIGINT
                ) WITHOUT ROWID;

                CREATE TABLE Contract (
                    Address TEXT PRIMARY KEY,
                    Owner TEXT,
                    Name TEXT,
                    Balance BIGINT,
                    Code BLOB,
                    EntryPoint BIGINT
                ) WITHOUT ROWID;
            ";

        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
                CREATE TABLE TransactionTransaction (
                    ParentId TEXT,
                    ChildId TEXT
                );

                CREATE TABLE ChainState (
                    Id INT PRIMARY KEY,
                    Weight BLOB,
                    Height BIGINT,
                    Blocks BIGINT,
                    LastHash TEXT,
                    CurrentDifficulty INTEGER
                );

                CREATE TABLE Token (
                    TokenId TEXT PRIMARY KEY,
                    IsConsumed BOOLEAN,
                    Ledger TEXT REFERENCES Ledger (Address),
                    Contract TEXT REFERENCES Contract (Address)
                ) WITHOUT ROWID;

                CREATE TABLE ContractSnapshot (
                    Id TEXT PRIMARY KEY,
                    Height INTEGER,
                    Snapshot BLOB,
                    Address TEXT REFERENCES Contract (Address)
                ) WITHOUT ROWID;
            ";

        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
                CREATE TABLE Effect (
                    Id TEXT PRIMARY KEY,
                    TransactionId TEXT REFERENCES Transactions (TransactionId),
                    TokenId TEXT REFERENCES Token (TokenId),
                    Sender TEXT,
                    Recipient TEXT,
                    Value BIGINT,
                    ConsumeToken BOOLEAN
                ) WITHOUT ROWID;
            ";

        cmd.ExecuteNonQuery();

        CreateIndex();
    }

    public void CreateIndex()
    {
        using var cmd = Connection.CreateCommand();

        cmd.CommandText = @"
                CREATE INDEX ix_tx_height ON Transactions (Height, TransactionType);
                CREATE INDEX ix_tx_from ON Transactions (Sender);
                CREATE INDEX ix_tx_to ON Transactions (Recipient);

                CREATE INDEX ix_txtx_parent ON TransactionTransaction (ParentId);
                CREATE INDEX ix_txtx_child ON TransactionTransaction (ChildId);

                CREATE INDEX effect_txid ON Effect (TransactionId);

                CREATE INDEX snapshot_height ON ContractSnapshot (Height);

                CREATE INDEX token_ledger ON Token (Ledger);
                CREATE INDEX token_contract ON Token (Contract);
            ";

        cmd.ExecuteNonQuery();
    }

    public void DropIndex()
    {
        using var cmd = Connection.CreateCommand();

        cmd.CommandText = @"
                DROP INDEX ix_tx_height;
                DROP INDEX ix_tx_from;
                DROP INDEX ix_tx_to;

                DROP INDEX ix_txtx_parent;
                DROP INDEX ix_txtx_child;

                DROP INDEX effect_txid;

                DROP INDEX snapshot_height;

                DROP INDEX token_ledger;
                DROP INDEX token_contract;
            ";

        cmd.ExecuteNonQuery();
    }
}
