﻿using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Configuration;
using NSec.Cryptography;
using Org.BouncyCastle.Crypto;
using RocksDbSharp;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Data.Entity;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Storage;

internal class RocksDBStorage : IStorage
{
    private RocksDb Database { get; set; }
    private ITransaction? CurrentTransaction { get; set; }
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

        CurrentKey = InitializeKey();
    }

    public ITransaction BeginTransaction()
    {
        return CurrentTransaction = new RocksDBTransaction(Database, this);
    }

    public bool Exists(string ixName, ReadOnlySpan<byte> key)
    {
        var ix = Database.GetColumnFamily(ixName);

        if (CurrentTransaction is not null)
        {
            using var baseIterator = Database.NewIterator(ix);
            using var iterator = CurrentTransaction.GetConnection().NewIterator(baseIterator);

            iterator.Seek(key);

            return iterator.Valid();
        }

        return Database.HasKey(key, ix);
    }

    public byte[]? Get(string ixName, byte[] key)
    {
        byte[] result;

        var ix = Database.GetColumnFamily(ixName);

        if (CurrentTransaction is not null)
        {
            result = CurrentTransaction.GetConnection().Get(Database, key, ix);
        }
        else
        {
            result = Database.Get(key, ix);
        }

        return result ?? default;
    }

    public T? Get<T>(string ixName, byte[] key)
    {
        byte[] result;

        var ix = Database.GetColumnFamily(ixName);

        if (CurrentTransaction is not null)
        {
            result = CurrentTransaction.GetConnection().Get(Database, key, ix);
        }
        else
        {
            result = Database.Get(key, ix);
        }

        if (result is null)
        {
            return default;
        }

        return MessagePackSerializer.Deserialize<T>(result);
    }

    public byte[][] GetMany(string ixName, byte[][] keys)
    {
        var ix = Database.GetColumnFamily(ixName);

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
        var ix = Database.GetColumnFamily(ixName);

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

    public void Put(string ixName, ReadOnlySpan<byte> key, byte[] bytes)
    {
        var ix = Database.GetColumnFamily(ixName);

        if (CurrentTransaction is not null)
        {
            CurrentTransaction.GetConnection().Put(key, bytes, ix);
            return;
        }

        Database.Put(key, bytes, ix);
    }

    public void Put<T>(string ixName, ReadOnlySpan<byte> key, T entity)
    {
        var ix = Database.GetColumnFamily(ixName);
        var bytes = MessagePackSerializer.Serialize<T>(entity);

        if (CurrentTransaction is not null)
        {
            CurrentTransaction.GetConnection().Put(key, bytes, ix);
            return;
        }

        Database.Put(key, bytes, ix);
    }

    public void Delete(string ixName, ReadOnlySpan<byte> key)
    {
        var ix = Database.GetColumnFamily(ixName);

        if (CurrentTransaction is not null)
        {
            CurrentTransaction.GetConnection().Delete(key, ix);
            return;
        }

        Database.Remove(key, ix);
    }

    public byte[]? FindFirst(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = Database.GetColumnFamily(ixName);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var baseIterator = Database.NewIterator(ix, readOptions);

        using var iterator = CurrentTransaction is not null ?
            CurrentTransaction.GetConnection().NewIterator(baseIterator, ix) :
            baseIterator;

        iterator.Seek(keyPrefix);

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public T? FindFirst<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = Database.GetColumnFamily(ixName);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var baseIterator = Database.NewIterator(ix, readOptions);

        using var iterator = CurrentTransaction is not null ?
            CurrentTransaction.GetConnection().NewIterator(baseIterator, ix) :
            baseIterator;

        iterator.Seek(keyPrefix);

        if (!iterator.Valid())
        {
            return default(T);
        }

        return MessagePackSerializer.Deserialize<T>(iterator.Value());
    }

    public List<byte[]> FindAll(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var ix = Database.GetColumnFamily(ixName);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);
        
        using var baseIterator = Database.NewIterator(ix, readOptions);

        using var iterator = CurrentTransaction is not null ?
            CurrentTransaction.GetConnection().NewIterator(baseIterator, ix) :
            baseIterator;

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
        var ix = Database.GetColumnFamily(ixName);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var baseIterator = Database.NewIterator(ix, readOptions);

        using var iterator = CurrentTransaction is not null ?
            CurrentTransaction.GetConnection().NewIterator(baseIterator, ix) :
            baseIterator;

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
        var ix = Database.GetColumnFamily(ixName);

        var readOptions = new ReadOptions();
        readOptions.SetPrefixSameAsStart(true);

        using var baseIterator = Database.NewIterator(ix, readOptions);

        using var iterator = CurrentTransaction is not null ?
            CurrentTransaction.GetConnection().NewIterator(baseIterator, ix) :
            baseIterator;

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
        var ix = Database.GetColumnFamily(ixName);
        using var baseIterator = Database.NewIterator(ix);

        using var iterator = CurrentTransaction is not null ?
            CurrentTransaction.GetConnection().NewIterator(baseIterator, ix) :
            baseIterator;

        iterator.SeekToLast();

        var results = new List<byte[]>();

        while (iterator.Valid())
        {
            results.Add(iterator.Value());
            iterator.Prev();
        }

        return results;
    }

    public ulong GetCurrentKey()
    {
        return CurrentKey;
    }

    public ulong NextKey()
    {
        var next = Interlocked.Increment(ref CurrentKey);

        if (CurrentTransaction is null)
        {
            var keyColumn = Database.GetColumnFamily("Key");
            var key = new byte[1];

            Database.Put(key, BitConverter.GetBytes(next), keyColumn);
        }

        return next;
    }

    private ulong InitializeKey()
    {
        var keyColumn = Database.GetColumnFamily("Key");

        var key = new byte[1];

        if (Database.HasKey(key))
        {
            return BitConverter.ToUInt64(Database.Get(key, keyColumn));
        }

        Database.Put(key, BitConverter.GetBytes(0), keyColumn);

        return 0;
    }
}