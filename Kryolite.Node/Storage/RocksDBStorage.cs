﻿using Kryolite.ByteSerializer;
using Kryolite.Node.Storage.Key;
using Kryolite.RocksDb;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node.Storage;

internal class RocksDBStorage : IStorage, IDisposable
{
    private RocksDb.RocksDb Database { get; set; }
    private ulong CurrentKey = 0;
    private readonly string StorePath;

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

    public RocksDb.RocksDb Open(string storePath)
    {
        var options = new DbOptions()
            .CreateIfMissing(true)
            .CreateMissingColumnFamilies(true)
            .WalDir(storePath)
            .KeepLogFileNum(1);

        var opts = new DbOptions()
            .CreateIfMissing(true)
            .CreateMissingColumnFamilies(true);

        var columns = new List<(string ColumnName, int KeySize, DbOptions ColumnOptions)>()
        {
            ("default", 0, opts),
            ("Key", 0, opts),
            (BlockKey.KeyName, BlockKey.KeySize, opts),
            (ViewKey.KeyName, ViewKey.KeySize, opts),
            (VoteKey.KeyName, VoteKey.KeySize, opts),
            (ChainStateKey.KeyName, ChainStateKey.KeySize, opts),
            (LedgerKey.KeyName, LedgerKey.KeySize, opts),
            (TransactionKey.KeyName, TransactionKey.KeySize, opts),
            (ContractKey.KeyName, ContractKey.KeySize, opts),
            (ContractCodeKey.KeyName, ContractCodeKey.KeySize, opts),
            (ContractSnapshotKey.KeyName, ContractSnapshotKey.KeySize, opts),
            (TokenKey.KeyName, TokenKey.KeySize, opts),
            (ValidatorKey.KeyName, ValidatorKey.KeySize, opts),
            (ViewHashKey.KeyName, ViewHashKey.KeySize, opts),
            (TokenIdKey.KeyName, TokenIdKey.KeySize, opts),
            (TokenLedgerKey.KeyName, TokenLedgerKey.KeySize, opts),
            (TransactionIdKey.KeyName, TransactionIdKey.KeySize, opts),
            (TransactionAddressKey.KeyName, TransactionAddressKey.KeySize, opts),
            (ScheduledTransactionKey.KeyName, ScheduledTransactionKey.KeySize, opts)
        };

        Database = new RocksDb.RocksDb(StorePath, options, columns);

        CurrentKey = InitializeKey();

        return Database;
    }

    public void Close()
    {
        Database.Dispose();
    }

    public ITransaction BeginTransaction()
    {
        return new RocksDBTransaction(Database, this);
    }

    public bool Exists(string ixName, ReadOnlySpan<byte> key)
    {
        return Database.HasKey(ixName, key);
    }

    public byte[]? Get(string ixName, ReadOnlySpan<byte> key)
    {
        var bytes = Database.Get(ixName, key);

        if (bytes.Length == 0)
        {
            return null;
        }
        
        return bytes;
    }

    public T? Get<T>(string ixName, ReadOnlySpan<byte> key) where T : ISerializable, new()
    {
        byte[] result = Database.Get(ixName, key);

        if (result.Length == 0)
        {
            return default;
        }

        return Serializer.Deserialize<T>(result);
    }

    public byte[][] GetMany(string ixName, byte[][] keys)
    {
        return Database.GetMany(ixName, keys);
    }

    public List<T> GetMany<T>(string ixName, byte[][] keys) where T : ISerializable, new()
    {
        var results = Database.GetMany(ixName, keys);
        var items = new List<T>(results.Length);

        foreach (var result in results)
        {
            if (result.Length == 0)
            {
                continue;
            }

            var data = Serializer.Deserialize<T>(result);

            if (data is not null)
            {
                items.Add(data);
            }
        }

        return items;
    }

    public void Put(string ixName, ReadOnlySpan<byte> key, ReadOnlySpan<byte> bytes, ITransaction? transaction = null)
    {
        if (transaction is not null)
        {
            transaction.GetConnection().Put(ixName, key, bytes);
            return;
        }

        Database.Put(ixName, key, bytes);
    }

    public void Put<T>(string ixName, ReadOnlySpan<byte> key, T entity, ITransaction? transaction = null) where T : ISerializable, new()
    {
        var bytes = Serializer.Serialize<T>(entity);

        if (transaction is not null)
        {
            transaction.GetConnection().Put(ixName, key, bytes);
            return;
        }

        Database.Put(ixName, key, bytes);
    }

    public void Delete(string ixName, ReadOnlySpan<byte> key, ITransaction? transaction = null)
    {
        if (transaction is not null)
        {
            transaction.GetConnection().Delete(ixName, key);
            return;
        }

        Database.Delete(ixName, key);
    }

    public byte[]? FindFirst(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var keySize = Database.KeySize(ixName);
        var upperBound = keyPrefix.ToArray();
        Array.Resize(ref upperBound, keySize);
        Array.Fill(upperBound, (byte)255, keyPrefix.Length, keySize - keyPrefix.Length);

        using var readOptions = new ReadOptions();
        readOptions.PrefixSameAsStart(true);
        readOptions.IterateUpperBound(upperBound);

        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.Seek(keyPrefix);

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public T? FindFirst<T>(string ixName, ReadOnlySpan<byte> keyPrefix) where T : ISerializable, new()
    {
        var keySize = Database.KeySize(ixName);
        var upperBound = keyPrefix.ToArray();
        Array.Resize(ref upperBound, keySize);
        Array.Fill(upperBound, (byte)255, keyPrefix.Length, keySize - keyPrefix.Length);

        using var readOptions = new ReadOptions();
        readOptions.PrefixSameAsStart(true);
        readOptions.IterateUpperBound(upperBound);

        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.Seek(keyPrefix);

        if (!iterator.Valid())
        {
            return default(T);
        }

        return Serializer.Deserialize<T>(iterator.Value());
    }

    public List<byte[]> FindAll(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var keySize = Database.KeySize(ixName);
        var upperBound = keyPrefix.ToArray();
        Array.Resize(ref upperBound, keySize);
        Array.Fill(upperBound, (byte)255, keyPrefix.Length, keySize - keyPrefix.Length);

        using var readOptions = new ReadOptions();
        readOptions.PrefixSameAsStart(true);
        readOptions.IterateUpperBound(upperBound);
        
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.Seek(keyPrefix);

        var results = new List<byte[]>();

        while (iterator.Valid())
        {
            results.Add(iterator.Value());
            iterator.Next();
        }

        return results;
    }

    public List<T> FindAll<T>(string ixName, ReadOnlySpan<byte> keyPrefix) where T : ISerializable, new()
    {
        var keySize = Database.KeySize(ixName);
        var upperBound = keyPrefix.ToArray();
        Array.Resize(ref upperBound, keySize);
        Array.Fill(upperBound, (byte)255, keyPrefix.Length, keySize - keyPrefix.Length);

        using var readOptions = new ReadOptions();
        readOptions.PrefixSameAsStart(true);
        readOptions.IterateUpperBound(upperBound);

        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.Seek(keyPrefix);

        var results = new List<T>();

        while (iterator.Valid())
        {
            var data = Serializer.Deserialize<T>(iterator.Value());

            if (data is not null)
            {
                results.Add(data);
            }

            iterator.Next();
        }

        return results;
    }

    public List<byte[]> FindAll(string ixName)
    {
        using var readOptions = new ReadOptions();
        readOptions.PrefixSameAsStart(true);

        using var iterator = Database.CreateIterator(ixName, readOptions);

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
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

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

    public List<T> FindLast<T>(string ixName, int count) where T : ISerializable, new()
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToLast();

        var results = new List<T>(Math.Max(count, 0));

        while (iterator.Valid())
        {
            var data = Serializer.Deserialize<T>(iterator.Value());

            if (data is not null)
            {
                results.Add(data);
            }

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
        var keySize = Database.KeySize(ixName);
        var key = keyPrefix.ToArray();
        Array.Resize(ref key, keySize);
        Array.Fill(key, (byte)255, keyPrefix.Length, keySize - keyPrefix.Length);

        var lowerBound = keyPrefix.ToArray();
        Array.Resize(ref lowerBound, keySize);

        using var readOptions = new ReadOptions();
        readOptions.IterateLowerBound(lowerBound);

        using var iterator = Database.CreateIterator(ixName, readOptions);
        iterator.SeekForPrev(key);

        var results = new List<byte[]>(Math.Max(count, 0));

        while (iterator.Valid())
        {
            var value = iterator.Value();

            if (value.Length != 0)
            {
                results.Add(iterator.Value());
            }

            if (results.Count == count)
            {
                break;
            }

            iterator.Prev();
        }

        return results;
    }

    public T? FindLast<T>(string ixName) where T : ISerializable, new()
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return default(T);
        }

        return Serializer.Deserialize<T>(iterator.Value());
    }

    public byte[]? FindLast(string ixName)
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public T? FindLast<T>(string ixName, ReadOnlySpan<byte> keyPrefix) where T : ISerializable, new()
    {
        var keySize = Database.KeySize(ixName);
        var key = keyPrefix.ToArray();
        Array.Resize(ref key, keySize);
        Array.Fill(key, byte.MaxValue, keyPrefix.Length, keySize - keyPrefix.Length);

        var lowerBound = keyPrefix.ToArray();
        Array.Resize(ref lowerBound, keySize);

        using var readOptions = new ReadOptions();
        readOptions.IterateLowerBound(lowerBound);

        using var iterator = Database.CreateIterator(ixName, readOptions);
        iterator.SeekForPrev(key);

        if (!iterator.Valid())
        {
            return default(T);
        }

        return Serializer.Deserialize<T>(iterator.Value());
    }

    public byte[]? FindLast(string ixName, ReadOnlySpan<byte> keyPrefix)
    {
        var keySize = Database.KeySize(ixName);
        var key = keyPrefix.ToArray();
        Array.Resize(ref key, keySize);
        Array.Fill(key, (byte)255, keyPrefix.Length, keySize - keyPrefix.Length);

        var lowerBound = keyPrefix.ToArray();
        Array.Resize(ref lowerBound, keySize);

        using var readOptions = new ReadOptions();
        readOptions.IterateLowerBound(lowerBound);

        using var iterator = Database.CreateIterator(ixName, readOptions);
        iterator.SeekForPrev(key);

        if (!iterator.Valid())
        {
            return null;
        }

        return iterator.Value();
    }

    public List<T> GetAll<T>(string ixName) where T : ISerializable, new()
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToFirst();

        var results = new List<T>();

        while (iterator.Valid())
        {
            var data = Serializer.Deserialize<T>(iterator.Value());

            if (data is not null)
            {
                results.Add(data);
            }

            iterator.Next();
        }

        return results;
    }

    public Iterator GetIterator(string ixName, ReadOptions readOpts)
    {
        return Database.CreateIterator(ixName, readOpts);
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
            Database.Put("Key", [], BitConverter.GetBytes(next));
        }

        return next;
    }

    private ulong InitializeKey()
    {
        if (Database.HasKey("Key", []))
        {
            return BitConverter.ToUInt64(Database.Get("Key", []));
        }

        Database.Put("Key", [], BitConverter.GetBytes(0UL));
        return 0;
    }

    public List<T> GetRange<T>(string ixName, int count, int toSkip) where T : ISerializable, new()
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

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

            var data = Serializer.Deserialize<T>(iterator.Value());

            if (data is not null)
            {
                results.Add(data);
            }

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
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

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
        Database.RecreateColumnFamily("Key");
        Database.RecreateColumnFamily(BlockKey.KeyName);
        Database.RecreateColumnFamily(ViewKey.KeyName);
        Database.RecreateColumnFamily(VoteKey.KeyName);
        Database.RecreateColumnFamily(ChainStateKey.KeyName);
        Database.RecreateColumnFamily(LedgerKey.KeyName);
        Database.RecreateColumnFamily(TransactionKey.KeyName);
        Database.RecreateColumnFamily(ContractKey.KeyName);
        Database.RecreateColumnFamily(ContractCodeKey.KeyName);
        Database.RecreateColumnFamily(ContractSnapshotKey.KeyName);
        Database.RecreateColumnFamily(TokenKey.KeyName);
        Database.RecreateColumnFamily(ValidatorKey.KeyName);
        Database.RecreateColumnFamily(ViewHashKey.KeyName);
        Database.RecreateColumnFamily(TokenIdKey.KeyName);
        Database.RecreateColumnFamily(TokenLedgerKey.KeyName);
        Database.RecreateColumnFamily(TransactionIdKey.KeyName);
        Database.RecreateColumnFamily(TransactionAddressKey.KeyName);
        Database.RecreateColumnFamily(ScheduledTransactionKey.KeyName);
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

    public void Compact()
    {
        Database.Compact();
    }

    public void Dispose()
    {
        Database.Dispose();
    }
}
