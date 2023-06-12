using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Configuration;
using NSec.Cryptography;
using Org.BouncyCastle.Crypto;
using RocksDbSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Data.Entity;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Kryolite.Node.Storage;

internal class RocksDBStorage : IStorage
{
    private RocksDb Database { get; set; }
    private Dictionary<string, ColumnFamilyHandle>  ColumnFamilies { get; } = new();


    private ulong CurrentKey = 0;

    public RocksDBStorage(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var storePath = Path.Combine(dataDir, "store");

        // TODO: Remove. Used only in testing
        if (Directory.Exists(storePath))
        {
            Directory.Delete(storePath, true);
        }

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

        using (var db = RocksDb.Open(options, storePath, new ColumnFamilies()))
        {
            db.CreateColumnFamily(opts, "Key");
            db.CreateColumnFamily(opts, "ChainState");
            db.CreateColumnFamily(opts, "Ledger");
            db.CreateColumnFamily(opts, "Transaction");
            db.CreateColumnFamily(opts, "ixTransactionId");
            db.CreateColumnFamily(opts, "ixTransactionAddress");
            db.CreateColumnFamily(opts, "ixTransactionHeight");
            db.CreateColumnFamily(opts, "ixChildless");
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

        ColumnFamilies.Add("Key", Database.GetColumnFamily("Key"));
        ColumnFamilies.Add("ChainState", Database.GetColumnFamily("ChainState"));
        ColumnFamilies.Add("Ledger", Database.GetColumnFamily("Ledger"));
        ColumnFamilies.Add("Transaction", Database.GetColumnFamily("Transaction"));
        ColumnFamilies.Add("ixTransactionId", Database.GetColumnFamily("ixTransactionId"));
        ColumnFamilies.Add("ixTransactionAddress", Database.GetColumnFamily("ixTransactionAddress"));
        ColumnFamilies.Add("ixTransactionHeight", Database.GetColumnFamily("ixTransactionHeight"));
        ColumnFamilies.Add("ixChildless", Database.GetColumnFamily("ixChildless"));

        CurrentKey = InitializeKey();
    }

    public ITransaction BeginTransaction()
    {
        return new RocksDBTransaction(Database, this);
    }

    public bool Exists(string ixName, ReadOnlySpan<byte> key)
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

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + 1;

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(upperBound.ToByteArray());

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

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + 1;

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(upperBound.ToByteArray());

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

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + 1;

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(upperBound.ToByteArray());
        
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

        var upperBound = new BigInteger(keyPrefix.ToArray(), true, true) + 1;

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        readOptions.SetIterateUpperBound(upperBound.ToByteArray());

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

        var results = new List<byte[]>(count);

        while (iterator.Valid())
        {
            results.Add(iterator.Value());
            iterator.Prev();

            if (results.Count == count)
            {
                break;
            }
        }

        return results;
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

        var key = new byte[1];

        if (Database.HasKey(key))
        {
            return BitConverter.ToUInt64(Database.Get(key, ix));
        }

        Database.Put(key, BitConverter.GetBytes(0), ix);

        return 0;
    }
}
