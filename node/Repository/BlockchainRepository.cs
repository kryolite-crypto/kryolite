using Dapper;
using DuckDB.NET;
using DuckDB.NET.Data;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Kryolite.Node.Repository;

public class BlockchainRepository : IBlockchainRepository
{
    private static SQLiteConnection? Connection { get; set; }

    public BlockchainRepository()
    {
        if (Connection is null)
        {
            var storePath = Path.Join(BlockchainService.DATA_PATH, "store.dat");
            File.Delete(storePath);

            SQLiteConnection.CreateFile(storePath);
            Connection = new SQLiteConnection($"Data Source={storePath};Version=3;");

            Connection.Open();

            Connection.Flags |= SQLiteConnectionFlags.NoVerifyTextAffinity;

            using var cmd = Connection.CreateCommand();

            cmd.CommandText = $@"
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
                );

                CREATE TABLE Ledger (
                    Address TEXT PRIMARY KEY,
                    Balance BIGINT,
                    Pending BIGINT
                );

                CREATE TABLE Contract (
                    Address TEXT PRIMARY KEY,
                    Owner TEXT,
                    Name TEXT,
                    Balance BIGINT,
                    Code BLOB,
                    EntryPoint BIGINT
                );
            ";

            cmd.ExecuteNonQuery();

            cmd.CommandText = $@"
                CREATE TABLE TransactionTransaction (
                    ParentId TEXT REFERENCES Transactions (TransactionId),
                    ChildId TEXT REFERENCES Transactions (TransactionId)
                );

                CREATE TABLE Vote (
                    Signature TEXT PRIMARY KEY,
                    PublicKey TEXT,
                    TransactionId TEXT
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
                );

                CREATE TABLE ContractSnapshot (
                    Id TEXT PRIMARY KEY,
                    Height INTEGER,
                    Snapshot BLOB,
                    Address TEXT REFERENCES Contract (Address)
                );
            ";

            cmd.ExecuteNonQuery();

            cmd.CommandText = $@"
                CREATE TABLE Effect (
                    Id TEXT PRIMARY KEY,
                    TransactionId TEXT REFERENCES Transactions (TransactionId),
                    TokenId TEXT REFERENCES Token (TokenId),
                    Sender TEXT,
                    Recipient TEXT,
                    Value BIGINT,
                    ConsumeToken BOOLEAN
                );
            ";

            cmd.ExecuteNonQuery();

            cmd.CommandText = $@"
                CREATE INDEX ix_tx_height ON Transactions (Height);
                CREATE INDEX ix_tx_from ON Transactions (Sender);
                CREATE INDEX ix_tx_to ON Transactions (Recipient);

                CREATE INDEX ix_txtx_parent ON TransactionTransaction (ParentId);
                CREATE INDEX ix_txtx_child ON TransactionTransaction (ChildId);

                CREATE INDEX vote_txid ON Vote (TransactionId);

                CREATE INDEX ledger_address ON Ledger (Address);

                CREATE INDEX effect_txid ON Effect (TransactionId);

                CREATE INDEX snapshot_height ON ContractSnapshot (Height);

                CREATE INDEX token_ledger ON Token (Ledger);
                CREATE INDEX token_contract ON Token (Contract);
            ";

            cmd.ExecuteNonQuery();

            cmd.CommandText = $@"
                pragma threads = 4;
                pragma journal_mode = wal; 
                pragma synchronous = normal;
                pragma locking_mode = exclusive;
                pragma temp_store = default; 
                pragma mmap_size = -1;";

            cmd.ExecuteNonQuery();
        }
    }

    public SQLiteTransaction BeginTransaction()
    {
        return Connection!.BeginTransaction();
    }

    public bool Exists(SHA256Hash transactionId)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                count(*)
            FROM
                Transactions
            WHERE
                TransactionId = @txid
        ";

        cmd.Parameters.Add(new SQLiteParameter("@txid", transactionId.ToString()));

        var count = (long)cmd.ExecuteScalar();

        return count > 0;
    }

    public Transaction? Get(SHA256Hash transactionId)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                TransactionId = @txid
        ";

        cmd.Parameters.Add(new SQLiteParameter("@txid", transactionId.ToString()));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        if (!reader.Read())
        {
            return null;
        }

        return Transaction.Read(reader);
    }

    public List<Transaction> GetPending()
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                ttx.ParentId,
                ttx.ChildId,
                tx.TransactionId,
                tx.TransactionType,
                tx.Height,
                tx.PublicKey,
                tx.Recipient,
                tx.Value,
                tx.Pow,
                tx.Data,
                tx.Timestamp,
                tx.Signature,
                tx.ExecutionResult
            FROM
                TransactionTransaction ttx
            JOIN
                Transactions tx ON ttx.ChildId = tx.TransactionId
            WHERE
                tx.Height IS NULL
        ";

        var lookup = new Dictionary<SHA256Hash, Transaction>();

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        while (reader.Read())
        {
            var parentId = (SHA256Hash)reader.GetString(0);
            var childId = (SHA256Hash)reader.GetString(1);

            ref var tx = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, childId, out var existed);

            if (!existed)
            {
                tx = Transaction.Read(reader, 2);
            }

            tx!.Parents.Add(parentId);
        }

        return lookup.Values.ToList();
    }

    public void UpdateStatus(List<Transaction> transactions)
    {
        var cmd = Connection!.CreateCommand();

        cmd.CommandText += $@"
                UPDATE
                    Transactions
                SET
                    Height = @height,
                    ExecutionResult = @status
                WHERE
                    TransactionId = @txid";

        foreach (var tx in transactions)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("txid", tx.TransactionId);
            cmd.Parameters.AddWithValue("height", tx.Height);
            cmd.Parameters.AddWithValue("status", (byte)tx.ExecutionResult);

            cmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);
        }
    }

    public List<SHA256Hash> GetParentHashes(SHA256Hash transactionId)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT 
                ValidatesId 
            FROM
                TransactionTransaction
            WHERE
                ChildId = @txid
        ";

        cmd.Parameters.Add(new SQLiteParameter("@txid", transactionId.ToString()));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        var results = new List<SHA256Hash>();

        if (!reader.HasRows)
        {
            return results;
        }

        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public void Add(Transaction tx)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO Transactions (
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Sender,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            ) VALUES (@txid, @txtype, @height, @pubk, @sender, @recipient, @value, @pow, @data, @timestamp, @sign, @result);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@txid", tx.TransactionId.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@txtype", (byte)tx.TransactionType));
        cmd.Parameters.Add(new SQLiteParameter("@height", tx.Height));
        cmd.Parameters.Add(new SQLiteParameter("@pubk", tx.PublicKey?.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@sender", tx.PublicKey?.ToAddress().ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@recipient", tx.To?.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@value", tx.Value));
        cmd.Parameters.Add(new SQLiteParameter("@pow", tx.Pow?.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@data", tx.Data));
        cmd.Parameters.Add(new SQLiteParameter("@timestamp", tx.Timestamp));
        cmd.Parameters.Add(new SQLiteParameter("@sign", tx.Signature?.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@result", (byte)tx.ExecutionResult));

        cmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);

        if (tx.TransactionType == TransactionType.GENESIS)
        {
            return;
        }

        using var refCmd = Connection!.CreateCommand();

        refCmd.CommandText = @"
                INSERT INTO TransactionTransaction (
                    ParentId,
                    ChildId
                ) VALUES (@parent, @child);
            ";

        refCmd.Parameters.Add(new SQLiteParameter("@parent"));
        refCmd.Parameters.Add(new SQLiteParameter("@child"));

        foreach (var parent in tx.Parents)
        {
            refCmd.Parameters[0].Value = parent.ToString();
            refCmd.Parameters[1].Value = tx.TransactionId.ToString();

            refCmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);
        }
    }

    public Genesis? GetGenesis()
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                TransactionType = @txtype
                AND
                Height = 0
        ";

        cmd.Parameters.Add(new SQLiteParameter("@txtype", (byte)TransactionType.GENESIS));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        if (!reader.Read())
        {
            return null;
        }

        return new Genesis(Transaction.Read(reader));
    }

    public View? GetLastView()
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                TransactionType = @txtype
            ORDER BY Height DESC LIMIT 1;
        ";

        cmd.Parameters.Add(new SQLiteParameter("@txtype", (byte)TransactionType.VIEW));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        if (!reader.Read())
        {
            return null;
        }

        return new View(Transaction.Read(reader));
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                Height = @height
                AND
                TransactionType = @txtype
        ";

        cmd.Parameters.Add(new SQLiteParameter("@height", height));
        cmd.Parameters.Add(new SQLiteParameter("@txtype", (byte)TransactionType.VOTE));

        using var reader = cmd.ExecuteReader();

        var results = new List<Vote>();

        while (reader.Read())
        {
            results.Add(new Vote(Transaction.Read(reader)));
        }

        return results;
    }

    public ChainState GetChainState()
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Weight,
                Height,
                Blocks,
                LastHash,
                CurrentDifficulty
            FROM
                ChainState
            WHERE
                Id = 0
        ";

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        var results = new List<Vote>();

        if (!reader.Read())
        {
            return new ChainState();
        }

        return ChainState.Read(reader);
    }

    public void CreateState(ChainState chainState)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO ChainState (
                Id,
                Weight,
                Height,
                Blocks,
                LastHash,
                CurrentDifficulty
            ) VALUES (0, @weight, @height, @blocks, @lasthash, @diff);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@weight", chainState.Weight.ToByteArray()));
        cmd.Parameters.Add(new SQLiteParameter("@height", chainState.Height));
        cmd.Parameters.Add(new SQLiteParameter("@blocks", chainState.Blocks));
        cmd.Parameters.Add(new SQLiteParameter("@lasthash", chainState.LastHash.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@diff", (int)chainState.CurrentDifficulty.Value));

        cmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);
    }

    public void SaveState(ChainState chainState)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            UPDATE
                ChainState
            SET
                Weight = @weight,
                Height = @height,
                Blocks = @blocks,
                LastHash = @lasthash,
                CurrentDifficulty = @diff
            WHERE
                Id = 0
        ";

        cmd.Parameters.Add(new SQLiteParameter("@weight", chainState.Weight.ToByteArray()));
        cmd.Parameters.Add(new SQLiteParameter("@height", chainState.Height));
        cmd.Parameters.Add(new SQLiteParameter("@blocks", chainState.Blocks));
        cmd.Parameters.Add(new SQLiteParameter("@lasthash", chainState.LastHash.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@diff", (int)chainState.CurrentDifficulty.Value));

        cmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);
    }

    /*public void Delete(Transaction tx)
    {
        Context.Transactions.Remove(tx);
        Context.SaveChanges();
    }*/

    /*public void DeleteContractSnapshot(long height)
    {
        var snapshots = Context.ContractSnapshots.Where(x => x.Height > height);

        Context.ContractSnapshots.RemoveRange(snapshots);
        Context.SaveChanges();
    }*/

    public Ledger? GetWallet(Address address)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Balance,
                Pending
            FROM
                Ledger
            WHERE
                Address = @addr
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        if (!reader.Read())
        {
            return null;
        }

        return Ledger.Read(reader);
    }

    public void UpdateWallet(Ledger wallet)
    {
        using var cmd = Connection!.CreateCommand();

        if (wallet.IsNew)
        {
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Ledger ( 
                    Address,
                    Balance,
                    Pending
                ) VALUES (@addr, @balance, @pending);
            ";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE
                    Ledger
                SET
                    Balance = @balance,
                    Pending = @pending
                WHERE
                    Address = @addr
            ";
        }

        cmd.Parameters.Add(new SQLiteParameter("@addr", wallet.Address.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@balance", (long)wallet.Balance));
        cmd.Parameters.Add(new SQLiteParameter("@pending", (long)wallet.Pending));

        cmd.ExecuteNonQuery(CommandBehavior.SequentialAccess);
    }

    public void UpdateWallets(IEnumerable<Ledger> wallets)
    {
        foreach (var wallet in wallets)
        {
            UpdateWallet(wallet);
        }
    }

    public void UpdateWallets(params Ledger[] wallets)
    {
        foreach (var wallet in wallets)
        {
            UpdateWallet(wallet);
        }
    }

    public Contract? GetContract(Address address)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Owner,
                Name,
                Balance,
                EntryPoint
            FROM
                Contract
            WHERE
                Address = @addr
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return Contract.Read(reader);
    }

    public List<Ledger> GetRichList(int count)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Balance,
                Pending
            FROM
                Ledger
            ORDER BY Balance DESC
            LIMIT @count
        ";

        cmd.Parameters.Add(new SQLiteParameter("@count", count));

        using var reader = cmd.ExecuteReader();

        var results = new List<Ledger>();

        while (reader.Read())
        {
            results.Add(Ledger.Read(reader));
        }

        return results;
    }

    public void AddContract(Contract contract)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO Contract ( 
                Address,
                Owner,
                Name,
                Balance,
                EntryPoint
            ) VALUES (@addr, @owner, @name, @balance, @entry);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", contract.Address.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@owner", contract.Owner.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@name", contract.Name));
        cmd.Parameters.Add(new SQLiteParameter("@balance", contract.Balance));
        cmd.Parameters.Add(new SQLiteParameter("@entry", contract.EntryPoint));

        cmd.ExecuteNonQuery();
    }

    public void UpdateContracts(IEnumerable<Contract> contracts)
    {
        foreach (var contract in contracts)
        {
            AddContract(contract);
        }
    }

    public void UpdateToken(Token token)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO Token ( 
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            ) VALUES (@id, @isconsumed, @ledger, @contract);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@tokenid", token.TokenId.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@isconsumed", token.IsConsumed));
        cmd.Parameters.Add(new SQLiteParameter("@ledger", token.Ledger.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@contract", token.Contract.ToString()));

        cmd.ExecuteNonQuery();
    }

    public void UpdateTokens(IEnumerable<Token> tokens)
    {
        foreach (var token in tokens)
        {
            UpdateToken(token);
        }
    }

    public List<Transaction> GetTransactions(Address address)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                Sender = @addr
                OR
                Recipient = @addr
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Transaction>();

        while (reader.Read())
        {
            results.Add(Transaction.Read(reader));
        }

        return results;
    }

    public List<SHA256Hash> GetTransactionsToValidate()
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId
            FROM
                Transactions
            WHERE
                TransactionId NOT IN (
                    SELECT ParentId FROM TransactionTransaction
                )
        ";

        using var reader = cmd.ExecuteReader();

        var results = new List<SHA256Hash>();

        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        if (results.Count < 2)
        {
            using var cmd2 = Connection!.CreateCommand();

            // return few extra to not get duplicates
            cmd2.CommandText = "SELECT TransactionId FROM Transactions ORDER BY Height DESC NULLS FIRST LIMIT @count";
            cmd2.Parameters.Add(new SQLiteParameter("@count", 2 + results.Count));

            using var reader2 = cmd2.ExecuteReader();

            while (reader2.Read())
            {
                var id = (SHA256Hash)reader2.GetString(0);
                
                if (!results.Contains(id))
                {
                    results.Add(id);
                }

                if (results.Count >= 2)
                {
                    break;
                }
            }
        }

        return results;
    }

    public Token? GetToken(SHA256Hash tokenId)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            FROM
                Token
            WHERE
                TokenId = @tokenid
        ";

        cmd.Parameters.Add(new SQLiteParameter("@tokenid", tokenId));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        if (!reader.Read())
        {
            return null;
        }

        return Token.Read(reader);
    }

    public Token? GetToken(Address ledger, SHA256Hash tokenId)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            FROM
                Token
            WHERE
                TokenId = @tokenid && Ledger = @ledger
        ";

        cmd.Parameters.Add(new SQLiteParameter("@tokenid", tokenId.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@ledger", ledger.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        if (!reader.Read())
        {
            return null;
        }

        return Token.Read(reader);
    }

    public List<Token> GetTokens(Address ledger)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            FROM
                Token
            WHERE
                Ledger = @ledger
        ";

        cmd.Parameters.Add(new SQLiteParameter("@ledger", ledger.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        while (reader.Read())
        {
            results.Add(Token.Read(reader));
        }

        return results;
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Address
            FROM
                Token
            WHERE
                ContractAddress = @contract
        ";

        cmd.Parameters.Add(new SQLiteParameter("@contract", contractAddress));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        while (reader.Read())
        {
            results.Add(Token.Read(reader));
        }

        return results;
    }
}
