using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Configuration;
using RocksDbSharp;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Kryolite.Node.Storage;

internal class RocksDBStorage : IStorage
{
    private RocksDb Database { get; set; }
    private Dictionary<string, ColumnFamilyHandle>  ColumnFamilies { get; } = new();
    private ulong CurrentKey = 0;
    private string StorePath;

    public RocksDBStorage(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        StorePath = Path.Combine(dataDir, "store");

        Database = Open(StorePath);

        var versionPath = Path.Join(dataDir, $"store.version.{Constant.STORE_VERSION}");

        if (!File.Exists(versionPath))
        {
            File.WriteAllText(versionPath, Constant.STORE_VERSION);
        }
    }

    public RocksDBStorage(string storePath)
    {
        StorePath = storePath;
        Database = Open(storePath);
    }

    public RocksDb Open(string storePath)
    {
        var options = new DbOptions()
            .SetCreateIfMissing(true)
            .SetWalDir(storePath)
            .SetKeepLogFileNum(1);

        var opts = new ColumnFamilyOptions()
            .SetCreateIfMissing(true)
            .SetCreateMissingColumnFamilies(true);

        if (!Directory.Exists(storePath))
        {
            using (var db = RocksDb.Open(options, storePath, new ColumnFamilies()))
            {
                db.CreateColumnFamily(opts, "Key");
                db.CreateColumnFamily(opts, "Block");
                db.CreateColumnFamily(opts, "View");
                db.CreateColumnFamily(opts, "Vote");
                db.CreateColumnFamily(opts, "ChainState");
                db.CreateColumnFamily(opts, "Ledger");
                db.CreateColumnFamily(opts, "Transaction");
                db.CreateColumnFamily(opts, "Contract");
                db.CreateColumnFamily(opts, "ContractCode");
                db.CreateColumnFamily(opts, "ContractSnapshot");
                db.CreateColumnFamily(opts, "Token");
                db.CreateColumnFamily(opts, "Validator");
                db.CreateColumnFamily(opts, "ixViewHash");
                db.CreateColumnFamily(opts, "ixTokenId");
                db.CreateColumnFamily(opts, "ixTokenLedger");
                db.CreateColumnFamily(opts, "ixTransactionId");
                db.CreateColumnFamily(opts, "ixTransactionAddress");
            }
        }

        var families = new ColumnFamilies
        {
            { "Key", opts },
            { "Block", opts },
            { "View", opts },
            { "Vote", opts },
            { "ChainState", opts },
            { "Ledger", opts },
            { "Transaction", opts },
            { "Contract", opts },
            { "ContractCode", opts },
            { "ContractSnapshot", opts },
            { "Token", opts },
            { "Validator", opts},
            { "ixViewHash", opts },
            { "ixTokenId", opts },
            { "ixTokenLedger", opts },
            { "ixTransactionId", opts },
            { "ixTransactionAddress", opts }
        };

        Database = RocksDb.Open(options, storePath, families);

        ColumnFamilies.Clear();

        ColumnFamilies.Add("Key", Database.GetColumnFamily("Key"));
        ColumnFamilies.Add("Block", Database.GetColumnFamily("Block"));
        ColumnFamilies.Add("View", Database.GetColumnFamily("View"));
        ColumnFamilies.Add("Vote", Database.GetColumnFamily("Vote"));
        ColumnFamilies.Add("ChainState", Database.GetColumnFamily("ChainState"));
        ColumnFamilies.Add("Ledger", Database.GetColumnFamily("Ledger"));
        ColumnFamilies.Add("Transaction", Database.GetColumnFamily("Transaction"));
        ColumnFamilies.Add("Contract", Database.GetColumnFamily("Contract"));
        ColumnFamilies.Add("ContractCode", Database.GetColumnFamily("ContractCode"));
        ColumnFamilies.Add("ContractSnapshot", Database.GetColumnFamily("ContractSnapshot"));
        ColumnFamilies.Add("Token", Database.GetColumnFamily("Token"));
        ColumnFamilies.Add("Validator", Database.GetColumnFamily("Validator"));
        ColumnFamilies.Add("ixViewHash", Database.GetColumnFamily("ixViewHash"));
        ColumnFamilies.Add("ixTokenId", Database.GetColumnFamily("ixTokenId"));
        ColumnFamilies.Add("ixTokenLedger", Database.GetColumnFamily("ixTokenLedger"));
        ColumnFamilies.Add("ixTransactionId", Database.GetColumnFamily("ixTransactionId"));
        ColumnFamilies.Add("ixTransactionAddress", Database.GetColumnFamily("ixTransactionAddress"));

        CurrentKey = InitializeKey();

        return Database;
    }

    public void Close()
    {
        ColumnFamilies.Clear();
        Database.Dispose();
    }

    public ITransaction BeginTransaction()
    {
        return new RocksDBTransaction(Database, this);
    }

    public bool Exists(string ixName, byte[] key)
    {
        var ix = ColumnFamilies[ixName];
        return Database.HasKey(key, ix);
    }

    public byte[]? Get(string ixName, byte[] key)
    {
        var ix = ColumnFamilies[ixName];
        return Database.Get(key, ix) ?? default;
    }

    public T? Get<T>(string ixName, byte[] key)
    {
        byte[] result;

        var ix = ColumnFamilies[ixName];

        result = Database.Get(key, ix);

        if (result is null)
        {
            return default;
        }

        return MessagePackSerializer.Deserialize<T>(result);
    }

    public byte[][] GetMany(string ixName, byte[][] keys)
    {
        var ix = ColumnFamilies[ixName];

        var handles = new ColumnFamilyHandle[keys.Length];
        Array.Fill(handles, ix);

        var resultCollection = Database.MultiGet(keys, handles);
        var results = new byte[resultCollection.Length][];

        for (int i = 0; i < resultCollection.Length; i++)
        {
            results[i] = resultCollection[i].Value;
        }

        return results;
    }

    public List<T> GetMany<T>(string ixName, byte[][] keys)
    {
        var ix = ColumnFamilies[ixName];

        var handles = new ColumnFamilyHandle[keys.Length];
        Array.Fill(handles, ix);

        var results = Database.MultiGet(keys, handles);
        var transactions = new List<T>(results.Length);

        foreach (var result in results)
        {
            transactions.Add(MessagePackSerializer.Deserialize<T>(result.Value));
        }

        return transactions;
    }

    public void Put(string ixName, ReadOnlySpan<byte> key, byte[] bytes, ITransaction? transaction = null)
    {
        var ix = ColumnFamilies[ixName];

        if (transaction is not null)
        {
            transaction.GetConnection().Put(key, bytes, ix);
            return;
        }

        Database.Put(key, bytes, ix);
    }

    public void Put<T>(string ixName, ReadOnlySpan<byte> key, T entity, ITransaction? transaction = null)
    {
        var ix = ColumnFamilies[ixName];
        var bytes = MessagePackSerializer.Serialize<T>(entity);

        if (transaction is not null)
        {
            transaction.GetConnection().Put(key, bytes, ix);
            return;
        }

        Database.Put(key, bytes, ix);
    }

    public void Delete(string ixName, ReadOnlySpan<byte> key, ITransaction? transaction = null)
    {
        var ix = ColumnFamilies[ixName];

        if (transaction is not null)
        {
            transaction.GetConnection().Delete(key, ix);
            return;
        }

        Database.Remove(key, ix);
    }

    public byte[]? FindFirst(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + BigInteger.One;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(bytes);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.Seek(keyPrefix);

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public T? FindFirst<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + BigInteger.One;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(bytes);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.Seek(keyPrefix);

        if (!iterator.Valid())
        {
            return default(T);
        }

        return MessagePackSerializer.Deserialize<T>(iterator.Value());
    }

    public List<byte[]> FindAll(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + BigInteger.One;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(bytes);
        
        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.Seek(keyPrefix);

        var results = new List<byte[]>();

        while (iterator.Valid())
        {
            results.Add(iterator.Value());
            iterator.Next();
        }

        return results;
    }

    public List<byte[]> FindAll(string ixName, ReadOnlySpan<byte> keyPrefix, ReadOnlySpan<byte> upperBound)
    {
        var ix = ColumnFamilies[ixName];

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(upperBound.ToArray());

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.Seek(keyPrefix);

        var results = new List<byte[]>();

        while (iterator.Valid())
        {
            results.Add(iterator.Value());
            iterator.Next();
        }

        return results;
    }

    public List<T> FindAll<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + BigInteger.One;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(bytes);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.Seek(keyPrefix);

        var results = new List<T>();

        while (iterator.Valid())
        {
            results.Add(MessagePackSerializer.Deserialize<T>(iterator.Value()));
            iterator.Next();
        }

        return results;
    }

    public List<byte[]> FindAll(string ixName)
    {
        var ix = ColumnFamilies[ixName];

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.SeekToFirst();

        var results = new List<byte[]>();

        while (iterator.Valid())
        {
            results.Add(iterator.Value());
            iterator.Next();
        }

        return results;
    }

    public List<byte[]> FindLast(string ixName, int count)
    {
        var ix = ColumnFamilies[ixName];
        using var iterator = Database.NewIterator(ix);

        iterator.SeekToLast();

        var results = new List<byte[]>(Math.Max(count, 0));

        while (iterator.Valid())
        {
            results.Add(iterator.Value());

            if (results.Count == count)
            {
                break;
            }

            iterator.Prev();
        }

        return results;
    }

    public List<T> FindLast<T>(string ixName, int count)
    {
        var ix = ColumnFamilies[ixName];
        using var iterator = Database.NewIterator(ix);

        iterator.SeekToLast();

        var results = new List<T>(Math.Max(count, 0));

        while (iterator.Valid())
        {
            results.Add(MessagePackSerializer.Deserialize<T>(iterator.Value()));

            if (results.Count == count)
            {
                break;
            }

            iterator.Prev();
        }

        return results;
    }

    public List<byte[]> FindLast(string ixName, ReadOnlySpan<byte> keyPrefix, int count)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + 1;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();
        readOptions.SetIterateLowerBound(keyPrefix.ToArray());
        readOptions.SetIterateUpperBound(bytes);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.SeekToLast();

        var results = new List<byte[]>(Math.Max(count, 0));

        while (iterator.Valid())
        {
            results.Add(iterator.Value());

            if (results.Count == count)
            {
                break;
            }

            iterator.Prev();
        }

        return results;
    }

    public T? FindLast<T>(string ixName)
    {
        var ix = ColumnFamilies[ixName];

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return default(T);
        }

        return MessagePackSerializer.Deserialize<T>(iterator.Value());
    }

    public byte[]? FindLast(string ixName)
    {
        var ix = ColumnFamilies[ixName];

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public T? FindLast<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + 1;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(bytes);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return default(T);
        }

        return MessagePackSerializer.Deserialize<T>(iterator.Value());
    }

    public byte[]? FindLast(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = ColumnFamilies[ixName];

        var upperBound = new BigInteger(keyPrefix, true, true) + BigInteger.One;

        var bytes = upperBound.ToByteArray();
        Array.Resize(ref bytes, keyPrefix.Length);
        Array.Reverse(bytes);

        var readOptions = new ReadOptions();

        readOptions.SetIterateLowerBound(keyPrefix.ToArray());
        readOptions.SetIterateUpperBound(bytes);

        using var iterator = Database.NewIterator(ix, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public List<T> GetAll<T>(string ixName)
    {
        var ix = ColumnFamilies[ixName];

        using var iterator = Database.NewIterator(ix);

        iterator.SeekToFirst();

        var results = new List<T>();

        while (iterator.Valid())
        {
            results.Add(MessagePackSerializer.Deserialize<T>(iterator.Value()));
            iterator.Next();
        }

        return results;
    }

    public Iterator GetIterator(string ixName, ReadOptions? readOpts = null)
    {
        var ix = ColumnFamilies[ixName];
        return Database.NewIterator(ix, readOpts);
    }

    public ulong GetCurrentKey()
    {
        return CurrentKey;
    }

    public ulong NextKey(ITransaction? transaction = null)
    {
        var next = Interlocked.Increment(ref CurrentKey);

        if (transaction is null)
        {
            var ix = ColumnFamilies["Key"];
            var key = new byte[1];

            Database.Put(key, BitConverter.GetBytes(next), ix);
        }

        return next;
    }

    private ulong InitializeKey()
    {
        var ix = ColumnFamilies["Key"];

        var key = new byte[0];

        if (Database.HasKey(key, ix))
        {
            return BitConverter.ToUInt64(Database.Get(key, ix));
        }

        Database.Put(key, BitConverter.GetBytes(0UL), ix);

        return 0;
    }

    public List<T> GetRange<T>(string ixName, int count, int toSkip)
    {
        var ix = ColumnFamilies[ixName];

        using var iterator = Database.NewIterator(ix);

        iterator.SeekToLast();

        var results = new List<T>(count);

        while (iterator.Valid())
        {
            toSkip--;

            if (toSkip > 0)
            {
                iterator.Prev();
                continue;
            }

            results.Add(MessagePackSerializer.Deserialize<T>(iterator.Value()));
            iterator.Prev();

            if (results.Count >= count)
            {
                break;
            }
        }

        return results;
    }

    public List<byte[]> GetRange(string ixName, int count, int toSkip)
    {
        var ix = ColumnFamilies[ixName];

        using var iterator = Database.NewIterator(ix);

        iterator.SeekToLast();

        var results = new List<byte[]>(count);

        while (iterator.Valid())
        {
            toSkip--;

            if (toSkip > 0)
            {
                iterator.Prev();
                continue;
            }

            results.Add(iterator.Value());

            if (results.Count >= count)
            {
                break;
            }

            iterator.Prev();
        }

        return results;
    }

    public void Reset()
    {
        Database.DropColumnFamily("Key");
        Database.DropColumnFamily("Block");
        Database.DropColumnFamily("View");
        Database.DropColumnFamily("Vote");
        Database.DropColumnFamily("ChainState");
        Database.DropColumnFamily("Ledger");
        Database.DropColumnFamily("Transaction");
        Database.DropColumnFamily("Contract");
        Database.DropColumnFamily("ContractCode");
        Database.DropColumnFamily("ContractSnapshot");
        Database.DropColumnFamily("Token");
        Database.DropColumnFamily("Validator");
        Database.DropColumnFamily("ixViewHash");
        Database.DropColumnFamily("ixTokenId");
        Database.DropColumnFamily("ixTokenLedger");
        Database.DropColumnFamily("ixTransactionNum");
        Database.DropColumnFamily("ixTransactionAddress");

        var opts = new ColumnFamilyOptions()
            .SetCreateIfMissing(true)
            .SetCreateMissingColumnFamilies(true);

        Database.CreateColumnFamily(opts, "Key");
        Database.CreateColumnFamily(opts, "Block");
        Database.CreateColumnFamily(opts, "View");
        Database.CreateColumnFamily(opts, "Vote");
        Database.CreateColumnFamily(opts, "ChainState");
        Database.CreateColumnFamily(opts, "Ledger");
        Database.CreateColumnFamily(opts, "Transaction");
        Database.CreateColumnFamily(opts, "Contract");
        Database.CreateColumnFamily(opts, "ContractCode");
        Database.CreateColumnFamily(opts, "ContractSnapshot");
        Database.CreateColumnFamily(opts, "Token");
        Database.CreateColumnFamily(opts, "Validator");
        Database.CreateColumnFamily(opts, "ixViewHash");
        Database.CreateColumnFamily(opts, "ixTokenId");
        Database.CreateColumnFamily(opts, "ixTokenLedger");
        Database.CreateColumnFamily(opts, "ixTransactionNum");
        Database.CreateColumnFamily(opts, "ixTransactionAddress");

        ColumnFamilies.Clear();
        ColumnFamilies.Add("Key", Database.GetColumnFamily("Key"));
        ColumnFamilies.Add("Block", Database.GetColumnFamily("Block"));
        ColumnFamilies.Add("View", Database.GetColumnFamily("View"));
        ColumnFamilies.Add("Vote", Database.GetColumnFamily("Vote"));
        ColumnFamilies.Add("ChainState", Database.GetColumnFamily("ChainState"));
        ColumnFamilies.Add("Ledger", Database.GetColumnFamily("Ledger"));
        ColumnFamilies.Add("Transaction", Database.GetColumnFamily("Transaction"));
        ColumnFamilies.Add("Contract", Database.GetColumnFamily("Contract"));
        ColumnFamilies.Add("ContractCode", Database.GetColumnFamily("ContractCode"));
        ColumnFamilies.Add("ContractSnapshot", Database.GetColumnFamily("ContractSnapshot"));
        ColumnFamilies.Add("Token", Database.GetColumnFamily("Token"));
        ColumnFamilies.Add("Validator", Database.GetColumnFamily("Validator"));
        ColumnFamilies.Add("ixViewHash", Database.GetColumnFamily("ixViewHash"));
        ColumnFamilies.Add("ixTokenId", Database.GetColumnFamily("ixTokenId"));
        ColumnFamilies.Add("ixTokenLedger", Database.GetColumnFamily("ixTokenLedger"));
        ColumnFamilies.Add("ixTransactionNum", Database.GetColumnFamily("ixTransactionNum"));
        ColumnFamilies.Add("ixTransactionAddress", Database.GetColumnFamily("ixTransactionAddress"));
    }

    public Checkpoint CreateCheckpoint()
    {
        var stagingDirectory = StorePath + ".staging";

        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, true);
        }

        var checkpoint = Database.Checkpoint();
        checkpoint.Save(stagingDirectory);

        return checkpoint;
    }
}
