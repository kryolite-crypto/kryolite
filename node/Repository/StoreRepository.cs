using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Data.Sqlite;
using NSec.Cryptography;
using RocksDbSharp;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Kryolite.Node.Repository;

public class StoreRepository : IStoreRepository, IDisposable
{
    private static RocksDb Database { get; set; }

    private static bool Recreated = false;
    private static ulong CurrentKey = 0;

    public StoreRepository()
    {
        var storePath = Path.Join(BlockchainService.DATA_PATH, "store");

        if (!Recreated)
        {
            if (Directory.Exists(storePath))
            {
                Directory.Delete(storePath, true);
            }

            Recreated = true;
        }

        if (Database is null)
        {
            var options = new DbOptions()
                .SetCreateIfMissing(true)
                .SetWalDir(storePath)
                .SetLevelCompactionDynamicLevelBytes(true)
                .SetBytesPerSync(1048576)
                .SetMaxBackgroundCompactions(Environment.ProcessorCount)
                .SetSoftPendingCompactionBytesLimit(49392123904)
                .SetHardPendingCompactionBytesLimit(98784247808);

            var opts = new ColumnFamilyOptions()
                .SetCreateIfMissing(true)
                .SetCreateMissingColumnFamilies(true)
                .IncreaseParallelism(4);

            using (var Database = RocksDb.Open(options, storePath, new ColumnFamilies()))
            {
                Database.CreateColumnFamily(opts, "Key");
                Database.CreateColumnFamily(opts, "ChainState");
                Database.CreateColumnFamily(opts, "Ledger");
                Database.CreateColumnFamily(opts, "Transaction");
                Database.CreateColumnFamily(opts, "ixTransactionId");
                Database.CreateColumnFamily(opts, "ixTransactionAddress");
                Database.CreateColumnFamily(opts, "ixTransactionHeight");
                Database.CreateColumnFamily(opts, "ixChildless");
            }

            var families = new ColumnFamilies();

            families.Add("Key", opts);
            families.Add("ChainState", opts);
            families.Add("Ledger", opts);
            families.Add("Transaction", opts);
            families.Add("ixTransactionId", opts);
            families.Add("ixTransactionAddress", opts);
            families.Add("ixTransactionHeight", opts);
            families.Add("ixChildless", opts);

            Database = RocksDb.Open(options, storePath, families);

            var keyColumn = Database.GetColumnFamily("Key");

            var key = new byte[1];

            if (Database.HasKey(key))
            {
                CurrentKey = BitConverter.ToUInt64(Database.Get(key, keyColumn));
            }

            Database.Put(key, BitConverter.GetBytes(CurrentKey), keyColumn);
        }
    }

    private ulong NextKey()
    {
        return ++CurrentKey;
    }

    public void Add(Transaction tx, WriteBatch? writeBatch = null)
    {
        writeBatch = null;
        bool write = false;
        if (writeBatch is null)
        {
            write = true;
            writeBatch = new WriteBatch();
        }

        var newEntity = tx.Id == 0;

        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();

        if (newEntity)
        {
            tx.Id = NextKey();
        }

        var id = BitConverter.GetBytes(((ulong?)tx.Id) ?? ulong.MaxValue);
        Array.Reverse(id);

        // Transaction
        writeBatch.Put(id, MessagePackSerializer.Serialize(tx), Database.GetColumnFamily("Transaction"));

        // TransactionId index
        writeBatch.Put(tx.TransactionId.Buffer, id, Database.GetColumnFamily("ixTransactionId"));

        // Address index
        var addrColumn = Database.GetColumnFamily("ixTransactionAddress");
        var addrKey = keyMem.Slice(0, 34);
        id.CopyTo(addrKey.Slice(26));

        if (tx.PublicKey is not null)
        {
            tx.From.Buffer.CopyTo(addrKey);
            writeBatch.Put(addrKey, id, addrColumn);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            writeBatch.Put(addrKey, id, addrColumn);
        }

        // Height, TransactionType index
        var height = BitConverter.GetBytes(((ulong?)tx.Height) ?? ulong.MaxValue);
        Array.Reverse(height);

        var heightKey = keyMem.Slice(0, 17);
        height.CopyTo(heightKey);
        heightKey[8] = (byte)tx.TransactionType;
        id.CopyTo(heightKey.Slice(9));

        writeBatch.Put(heightKey, id, Database.GetColumnFamily("ixTransactionHeight"));

        // Childless index
        var childlessColumn = Database.GetColumnFamily("ixChildless");
        writeBatch.Put(tx.TransactionId.Buffer, id, childlessColumn);

        foreach (var parent in tx.Parents)
        {
            writeBatch.Delete(parent.Buffer, childlessColumn);
        }

        ArrayPool<byte>.Shared.Return(keyBuf);

        if (write)
        {
            Database.Write(writeBatch);
            writeBatch.Dispose();
        }
    }

    public unsafe void Finalize(List<Transaction> transactions, WriteBatch? writeBatch = null)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        if (writeBatch is null)
        {
            writeBatch = new WriteBatch();
        }

        var heightColumn = Database.GetColumnFamily("ixTransactionHeight");
        var nullHeight = BitConverter.GetBytes(ulong.MaxValue);

        var key = ArrayPool<byte>.Shared.Rent(17);

        for (var i = 0; i < transactions.Count; i++)
        {
            key[8] = (byte)transactions[i].TransactionType;

            var id = BitConverter.GetBytes(transactions[i].Id);
            Array.Reverse(id);
            id.CopyTo(key, 9);

            var height = BitConverter.GetBytes(((ulong?)transactions[i].Height) ?? ulong.MaxValue);
            Array.Reverse(height);

            nullHeight.CopyTo(key, 0);
            writeBatch.Delete(key, heightColumn);

            height.CopyTo(key, 0);
            writeBatch.Put(key, id, heightColumn);
        }

        // TODO: Move these to Write()
        // update incremented primarykey
        var pk = new byte[1];
        writeBatch.Put(pk, BitConverter.GetBytes(CurrentKey), Database.GetColumnFamily("Key"));

        ArrayPool<byte>.Shared.Return(key);
    }

    public void Write(WriteBatch writeBatch)
    {
        var key = new byte[1];
        writeBatch.Put(key, BitConverter.GetBytes(CurrentKey), Database.GetColumnFamily("Key"));
        Database.Write(writeBatch);
    }

    public bool Exists(SHA256Hash transactionId)
    {
        return Database.HasKey(transactionId.Buffer);
    }

    public Transaction? Get(SHA256Hash transactionId)
    {
        var txidColumn = Database.GetColumnFamily("ixTransactionId");

        if (!Database.HasKey(transactionId.Buffer, txidColumn))
        {
            return null;
        }

        var key = Database.Get(transactionId.Buffer, txidColumn);
        var tx = MessagePackSerializer.Deserialize<Transaction>(Database.Get(key, Database.GetColumnFamily("Transaction")));

        return tx;
    }

    public List<Transaction> GetPending()
    {
        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        var height = BitConverter.GetBytes(uint.MaxValue);

        using var iterator = Database.NewIterator(Database.GetColumnFamily("ixTransactionHeight"), readOptions);

        iterator.Seek(height);

        var ids = new List<byte[]>();

        while (iterator.Valid())
        {
            ids.Add(iterator.Value());

            iterator.Next();
        }

        var handle = Database.GetColumnFamily("Transaction");
        var handles = new ColumnFamilyHandle[ids.Count];

        Array.Fill(handles, handle);

        var results = Database.MultiGet(ids.ToArray(), handles);

        var transactions = new List<Transaction>(results.Length);

        foreach (var result in results)
        {
            transactions.Add(MessagePackSerializer.Deserialize<Transaction>(result.Value));
        }

        return transactions;
    }

    public Genesis? GetGenesis()
    {
        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        var heightColumn = Database.GetColumnFamily("ixTransactionHeight");
        var txColumn = Database.GetColumnFamily("Transaction");

        var key = new byte[9];
        key[8] = (byte)TransactionType.GENESIS;

        using var iterator = Database.NewIterator(heightColumn, readOptions);

        iterator.Seek(key);

        if (!iterator.Valid())
        {
            return null;
        }

        if (!Database.HasKey(iterator.Value(), txColumn))
        {
            return null;
        }

        return new Genesis(MessagePackSerializer.Deserialize<Transaction>(Database.Get(iterator.Value(), txColumn)));
    }

    public View? GetLastView()
    {
        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        var chainColumn = Database.GetColumnFamily("ChainState");
        var heightColumn = Database.GetColumnFamily("ixTransactionHeight");
        var txColumn = Database.GetColumnFamily("Transaction");

        long height = 0;

        var chainKey = new byte[1];

        if (Database.HasKey(chainKey, chainColumn))
        {
            height = MessagePackSerializer.Deserialize<ChainState>(Database.Get(chainKey, chainColumn)).Height;
        }

        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        var key = new byte[9];
        key[8] = (byte)TransactionType.VIEW;

        heightBytes.CopyTo(key, 0);

        using var iterator = Database.NewIterator(heightColumn, readOptions);

        iterator.Seek(key);

        if (!iterator.Valid())
        {
            return null;
        }

        if (!Database.HasKey(iterator.Value(), txColumn))
        {
            return null;
        }

        return new View(MessagePackSerializer.Deserialize<Transaction>(Database.Get(iterator.Value(), txColumn)));
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        var heightColumn = Database.GetColumnFamily("ixTransactionHeight");
        var txColumn = Database.GetColumnFamily("Transaction");

        var key = BitConverter.GetBytes(height);
        Array.Reverse(key);
        Array.Resize(ref key, 9);
        key[8] = (byte)TransactionType.VOTE;

        var upperBound = key;
        upperBound[8] = (byte)TransactionType.VOTE + 1;
        readOptions.SetIterateUpperBound(upperBound);

        using var iterator = Database.NewIterator(heightColumn, readOptions);

        iterator.Seek(key);

        var ids = new List<byte[]>();

        while (iterator.Valid())
        {
            ids.Add(iterator.Value());
            iterator.Next();
        }

        var families = new ColumnFamilyHandle[ids.Count];

        Array.Fill(families, txColumn);

        var results = Database.MultiGet(ids.ToArray(), families);
        var votes = new List<Vote>(results.Length);

        foreach (var result in results)
        {
            var tx = MessagePackSerializer.Deserialize<Transaction>(result.Value);
            votes.Add(new Vote(tx));
        }

        return votes;
    }

    public ChainState? GetChainState()
    {
        var chainColumn = Database.GetColumnFamily("ChainState");
        var chainKey = new byte[1];

        if (!Database.HasKey(chainKey, chainColumn))
        {
            return null;
        }

        return MessagePackSerializer.Deserialize<ChainState>(Database.Get(chainKey, chainColumn));
    }

    public void SaveState(ChainState chainState, WriteBatch? writeBatch = null)
    {
        bool write = false;

        if (writeBatch is null)
        {
            write = true;
            writeBatch = new WriteBatch();
        }

        var chainColumn = Database.GetColumnFamily("ChainState");
        var chainKey = new byte[1];

        writeBatch.Put(chainKey, MessagePackSerializer.Serialize<ChainState>(chainState), chainColumn);

        if (write)
        {
            Database.Write(writeBatch);
            writeBatch.Dispose();
        }
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

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
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
            FROM
                Transactions
            WHERE
                Sender = @addr OR Recipient = @addr
            ORDER BY DESC Height, Timestamp
            LIMIT @count
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@count", count));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        var results = new List<Transaction>();

        while (reader.Read())
        {
            results.Add(Transaction.Read(reader));
        }

        return results;*/

        return new();
    }

    public Ledger? GetWallet(Address address)
    {
        var ledgerColumn = Database.GetColumnFamily("Ledger");

        if (!Database.HasKey(address.Buffer, ledgerColumn))
        {
            return null;
        }

        return MessagePackSerializer.Deserialize<Ledger>(Database.Get(address.Buffer, ledgerColumn));
    }

    public void UpdateWallet(Ledger ledger, WriteBatch? writeBatch = null)
    {
        bool write = false;
        if (writeBatch is null)
        {
            write = true;
            writeBatch = new WriteBatch();
        }
        /*using var cmd = Connection!.CreateCommand();

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

        wallet.IsNew = false;*/
        var ledgerColumn = Database.GetColumnFamily("Ledger");

        writeBatch.Put(ledger.Address.Buffer, MessagePackSerializer.Serialize(ledger), ledgerColumn);

        if (write)
        {
            Database.Write(writeBatch);
            writeBatch.Dispose();
        }
    }

    public void UpdateWallets(IEnumerable<Ledger> ledgers, WriteBatch? writeBatch = null)
    {
        bool write = false;
        if (writeBatch is null)
        {
            write = true;
            writeBatch = new WriteBatch();
        }

        foreach (var ledger in ledgers)
        {
            UpdateWallet(ledger);
        }

        if (write)
        {
            Database.Write(writeBatch);
            writeBatch.Dispose();
        }
    }

    public void UpdateWallets(WriteBatch? writeBatch = null, params Ledger[] ledgers)
    {
        bool write = false;
        if (writeBatch is null)
        {
            write = true;
            writeBatch = new WriteBatch();
        }

        foreach (var ledger in ledgers)
        {
            UpdateWallet(ledger);
        }

        if (write)
        {
            Database.Write(writeBatch);
            writeBatch.Dispose();
        }
    }

    public Contract? GetContract(Address address)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        return Contract.Read(reader);*/
        return null;
    }

    public List<Ledger> GetRichList(int count)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        return results;*/
        return new();
    }

    public void AddContract(Contract contract)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        cmd.ExecuteNonQuery();*/
    }

    public void UpdateContracts(IEnumerable<Contract> contracts)
    {
        /*foreach (var contract in contracts)
        {
            AddContract(contract);
        }*/
    }

    public void UpdateToken(Token token)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        cmd.ExecuteNonQuery();*/
    }

    public void UpdateTokens(IEnumerable<Token> tokens)
    {
        /*foreach (var token in tokens)
        {
            UpdateToken(token);
        }*/
    }

    public List<Transaction> GetTransactions(Address address)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
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

        return results;*/
        return new();
    }

    public List<SHA256Hash> GetTransactionsToValidate()
    {
        var column = Database.GetColumnFamily("ixChildless");

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var iterator = Database.NewIterator(column, readOptions);

        iterator.SeekToFirst();

        var hashes = new List<SHA256Hash>();

        while (iterator.Valid())
        {
            hashes.Add(iterator.Key());
            iterator.Next();
        }

        if (hashes.Count < 2)
        {
            var heightColumn = Database.GetColumnFamily("ixTransactionHeight");
            var txColumn = Database.GetColumnFamily("Transaction");

            using var hIterator = Database.NewIterator(heightColumn, readOptions);

            iterator.SeekToLast();

            while (iterator.Valid())
            {
                var tx = MessagePackSerializer.Deserialize<Transaction>(Database.Get(iterator.Value(), txColumn));
                hashes.Add(tx.TransactionId);

                if (hashes.Count >= 2)
                {
                    break;
                }

                iterator.Prev();
            }
        }

        return hashes;
    }

    public Token? GetToken(SHA256Hash tokenId)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        return Token.Read(reader);*/
        return null;
    }

    public Token? GetToken(Address ledger, SHA256Hash tokenId)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        return Token.Read(reader);*/
        return null;
    }

    public List<Token> GetTokens(Address ledger)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        return results;*/
        return new();
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        /*using var cmd = Connection!.CreateCommand();

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

        return results;*/
        return new();
    }

    public void Dispose()
    {

    }

    public long? GetTimestamp(SHA256Hash transactionId)
    {
        return Get(transactionId)?.Timestamp;
        // throw new NotImplementedException();
    }
}
