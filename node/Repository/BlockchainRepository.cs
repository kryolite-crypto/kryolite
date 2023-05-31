using DuckDB.NET.Data;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Repository;

public class BlockchainRepository : IBlockchainRepository
{
    private static DuckDBConnection? Connection { get; set; }

    public BlockchainRepository()
    {
        if (Connection is null)
        {
            var storePath = Path.Join(BlockchainService.DATA_PATH, "store.dat");
            Connection = new DuckDBConnection($"Data Source={storePath}");

            Connection.Open();

            using var cmd = Connection.CreateCommand();

            cmd.CommandText = $@"
                CREATE TABLE IF NOT EXISTS Transaction (
                    TransactionId VARCHAR PRIMARY KEY,
                    TransactionType TINYINT,
                    Height LONG,
                    PublicKey VARCHAR,
                    Sender VARCHAR,
                    Recipient VARCHAR,
                    Value BIGINT,
                    Pow VARCHAR,
                    Data BLOB,
                    Timestamp BIGINT,
                    Signature VARCHAR,
                    ExecutionResult TINYINT
                );

                CREATE TABLE IF NOT EXISTS Ledger (
                    Address VARCHAR PRIMARY KEY,
                    Balance BIGINT,
                    Pending BIGINT,
                );

                CREATE TABLE IF NOT EXISTS Contract (
                    Address VARCHAR PRIMARY KEY,
                    Owner VARCHAR,
                    Name VARCHAR,
                    Balance BIGINT,
                    Code BLOB,
                    EntryPoint BIGINT
                );
            ";
            
            cmd.ExecuteNonQuery();

            using var cmd2 = Connection.CreateCommand();

            cmd2.CommandText = $@"
                CREATE TABLE IF NOT EXISTS TransactionTransaction (
                    ParentId VARCHAR, -- REFERENCES Transaction (TransactionId),
                    ChildId VARCHAR, -- REFERENCES Transaction (TransactionId),
                    Height INTEGER
                );

                CREATE TABLE IF NOT EXISTS Vote (
                    Signature VARCHAR PRIMARY KEY,
                    PublicKey VARCHAR,
                    TransactionId VARCHAR
                );

                CREATE TABLE IF NOT EXISTS ChainState (
                    Id INT PRIMARY KEY,
                    Weight BLOB,
                    Height BIGINT,
                    Blocks BIGINT,
                    LastHash VARCHAR,
                    CurrentDifficulty INTEGER
                );

                CREATE TABLE IF NOT EXISTS Token (
                    TokenId VARCHAR PRIMARY KEY,
                    IsConsumed BOOLEAN,
                    Ledger VARCHAR REFERENCES Ledger (Address),
                    Contract VARCHAR REFERENCES Contract (Address)
                );

                CREATE TABLE IF NOT EXISTS ContractSnapshot (
                    Id VARCHAR PRIMARY KEY,
                    Height INTEGER,
                    Snapshot BLOB,
                    Address VARCHAR REFERENCES Contract (Address)
                );
            ";

            cmd2.ExecuteNonQuery();

            using var cmd3 = Connection.CreateCommand();

            cmd3.CommandText = $@"
                CREATE TABLE IF NOT EXISTS Effect (
                    Id VARCHAR PRIMARY KEY,
                    TransactionId VARCHAR REFERENCES Transaction (TransactionId),
                    TokenId VARCHAR REFERENCES Token (TokenId),
                    Sender VARCHAR,
                    Recipient VARCHAR,
                    Value BIGINT,
                    ConsumeToken BOOLEAN
                );
            ";

            cmd3.ExecuteNonQuery();

            using var cmd4 = Connection.CreateCommand();

            cmd4.CommandText = $@"
                -- CREATE INDEX ix_tx_height ON Transaction (Height);
                CREATE INDEX ix_tx_from ON Transaction (Sender);
                CREATE INDEX ix_tx_to ON Transaction (Recipient);

                CREATE INDEX ix_tx_parent ON TransactionTransaction (ParentId);
                CREATE INDEX ix_tx_child ON TransactionTransaction (ChildId);

                CREATE INDEX vote_txid ON Vote (TransactionId);

                CREATE INDEX ledger_address ON Ledger (Address);

                CREATE INDEX effect_txid ON Effect (TransactionId);

                CREATE INDEX snapshot_height ON ContractSnapshot (Height);

                CREATE INDEX token_ledger ON Token (Ledger);
                CREATE INDEX token_contract ON Token (Contract);
            ";

            cmd4.ExecuteNonQuery();
        }
    }

    public DuckDBTransaction BeginTransaction()
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
                Transaction
            WHERE
                TransactionId = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(transactionId.ToString()));

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
                Transaction
            WHERE
                TransactionId = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(transactionId.ToString()));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return Transaction.Read(reader);
    }

    public List<Transaction> GetParents(SHA256Hash transactionId)
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
                Transaction
            WHERE
                TransactionId IN (
                    SELECT ParentId FROM TransactionTransaction WHERE Height IS NULL
                )
        ";

        //cmd.Parameters.Add(new DuckDBParameter(transactionId.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Transaction>();

        while (reader.Read())
        {
            results.Add(Transaction.Read(reader));
        }

        return results;
    }

    public void UpdateHeight(Transaction transaction)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            UPDATE
                Transaction
            SET
                Height = ?
            WHERE
                TransactionId = ?;

            UPDATE
                TransactionTransaction
            SET
                Height = ?
            WHERE
                ParentId = ?;
        ";

        cmd.Parameters.Add(new DuckDBParameter(transaction.Height));
        cmd.Parameters.Add(new DuckDBParameter(transaction.TransactionId.ToString()));

        cmd.ExecuteNonQuery();
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
                ChildId = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(transactionId.ToString()));

        using var reader = cmd.ExecuteReader();

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
            INSERT INTO Transaction (
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
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
        ";

        cmd.Parameters.Add(new DuckDBParameter(tx.TransactionId.ToString()));
        cmd.Parameters.Add(new DuckDBParameter((byte)tx.TransactionType));
        cmd.Parameters.Add(new DuckDBParameter(tx.Height));
        cmd.Parameters.Add(new DuckDBParameter(tx.PublicKey?.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(tx.PublicKey?.ToAddress().ToString()));
        cmd.Parameters.Add(new DuckDBParameter(tx.To?.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(tx.Value));
        cmd.Parameters.Add(new DuckDBParameter(tx.Pow?.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(tx.Data));
        cmd.Parameters.Add(new DuckDBParameter(tx.Timestamp));
        cmd.Parameters.Add(new DuckDBParameter(tx.Signature?.ToString()));
        cmd.Parameters.Add(new DuckDBParameter((byte)tx.ExecutionResult));

        cmd.ExecuteNonQuery();

        if (tx.TransactionType == TransactionType.GENESIS)
        {
            return;
        }

        foreach (var parent in tx.Parents)
        {
            using var refCmd = Connection!.CreateCommand();

            refCmd.CommandText = @"
                INSERT INTO TransactionTransaction (
                    ParentId,
                    ChildId
                ) VALUES (?, ?);
            ";

            refCmd.Parameters.Add(new DuckDBParameter(parent.ToString()));
            refCmd.Parameters.Add(new DuckDBParameter(tx.TransactionId.ToString()));

            refCmd.ExecuteNonQuery();
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
                Transaction
            WHERE
                TransactionType = ?
                AND
                Height = 0
        ";

        cmd.Parameters.Add(new DuckDBParameter((byte)TransactionType.GENESIS));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new Genesis(Transaction.Read(reader));
    }

    public View GetLastView(bool includeVotes = false)
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
                Transaction
            WHERE
                TransactionType = ?
            ORDER BY Height DESC LIMIT 1;
        ";

        cmd.Parameters.Add(new DuckDBParameter((byte)TransactionType.VIEW));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            throw new Exception("view not found");
        }

        var view = new View(Transaction.Read(reader));

        if (includeVotes)
        {
            view.Votes = GetVotes(view.TransactionId);
        }

        return view;
    }

    public bool VoteExists(Signature signature)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                count(*)
            FROM
                Vote
            WHERE
                Signature = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(signature.ToString()));

        var count = (long)cmd.ExecuteScalar();

        return count > 0;
    }

    public void AddVote(Vote vote)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO Vote ( 
                Signature,
                PublicKey,
                TransactionId
            ) VALUES (?, ?, ?, ?, ?);
        ";

        cmd.Parameters.Add(new DuckDBParameter(vote.Signature.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(vote.PublicKey.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(vote.TransactionId.ToString()));

        cmd.ExecuteNonQuery();
    }

    public List<Vote> GetVotes(SHA256Hash transactionId)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Signature,
                PublicKey,
                TransactionId
            FROM
                Vote
            WHERE
                TransactionId = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(transactionId.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Vote>();

        while (reader.Read())
        {
            results.Add(Vote.Read(reader));
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

        using var reader = cmd.ExecuteReader();

        var results = new List<Vote>();

        if (!reader.Read())
        {
            return new ChainState();
        }

        return ChainState.Read(reader);
    }

    public void SaveState(ChainState chainState)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO ChainState (
                Id,
                Weight,
                Height,
                Blocks,
                LastHash,
                CurrentDifficulty
            ) VALUES (0, ?, ?, ?, ?, ?);
        ";

        cmd.Parameters.Add(new DuckDBParameter(chainState.Weight.ToByteArray()));
        cmd.Parameters.Add(new DuckDBParameter(chainState.Height));
        cmd.Parameters.Add(new DuckDBParameter(chainState.Blocks));
        cmd.Parameters.Add(new DuckDBParameter(chainState.LastHash.ToString()));
        cmd.Parameters.Add(new DuckDBParameter((int)chainState.CurrentDifficulty.Value));

        cmd.ExecuteNonQuery();
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
                Address = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(address.ToString()));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return Ledger.Read(reader);
    }

    public void UpdateWallet(Ledger wallet)
    {
        using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO Ledger ( 
                Address,
                Balance,
                Pending
            ) VALUES (?, ?, ?);
        ";

        cmd.Parameters.Add(new DuckDBParameter(wallet.Address.ToString()));
        cmd.Parameters.Add(new DuckDBParameter((long)wallet.Balance));
        cmd.Parameters.Add(new DuckDBParameter((long)wallet.Pending));

        cmd.ExecuteNonQuery();
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
                Address = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(address.ToString()));

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
            WHERE
                Address = ?
            ORDER BY Balance DESC
            LIMIT ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(count));

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
            INSERT OR REPLACE INTO Contract ( 
                Address,
                Owner,
                Name,
                Balance,
                EntryPoint
            ) VALUES (?, ?, ?, ?, ?);
        ";

        cmd.Parameters.Add(new DuckDBParameter(contract.Address.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(contract.Owner.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(contract.Name));
        cmd.Parameters.Add(new DuckDBParameter(contract.Balance));
        cmd.Parameters.Add(new DuckDBParameter(contract.EntryPoint));

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
            ) VALUES (?, ?, ?);
        ";

        cmd.Parameters.Add(new DuckDBParameter(token.TokenId.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(token.IsConsumed));
        cmd.Parameters.Add(new DuckDBParameter(token.Ledger.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(token.Contract.ToString()));

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
                Transaction
            WHERE
                Sender = ?
                OR
                Recipient = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(address.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(address.ToString()));

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
                Transaction
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
            cmd2.CommandText = "SELECT TransactionId FROM Transaction ORDER BY Height DESC NULLS FIRST LIMIT ?";
            cmd2.Parameters.Add(new DuckDBParameter(2 + results.Count));

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
                TokenId = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(tokenId));

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
                TokenId = ? && Ledger = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(tokenId.ToString()));
        cmd.Parameters.Add(new DuckDBParameter(ledger.ToString()));

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
                Ledger = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(ledger.ToString()));

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
                ContractAddress = ?
        ";

        cmd.Parameters.Add(new DuckDBParameter(contractAddress));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        while (reader.Read())
        {
            results.Add(Token.Read(reader));
        }

        return results;
    }
}
