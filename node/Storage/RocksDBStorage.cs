using Kryolite.RocksDb;
using Kryolite.Shared;
using MemoryPack;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node.Storage;

internal class RocksDBStorage : IStorage
{
    private RocksDb.RocksDb Database { get; set; }
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
            ("Block", SHA256Hash.HASH_SZ, opts),
            ("View", sizeof(long), opts),
            ("Vote", SHA256Hash.HASH_SZ, opts),
            ("ChainState", sizeof(long), opts),
            ("Ledger", Address.ADDRESS_SZ, opts),
            ("Transaction", sizeof(long), opts),
            ("Contract", Address.ADDRESS_SZ, opts),
            ("ContractCode", Address.ADDRESS_SZ, opts),
            ("ContractSnapshot", Address.ADDRESS_SZ + sizeof(long), opts),
            ("Token", sizeof(long), opts),
            ("Validator", Address.ADDRESS_SZ, opts),
            ("ixViewHash", SHA256Hash.HASH_SZ, opts),
            ("ixTokenId", Address.ADDRESS_SZ + SHA256Hash.HASH_SZ, opts),
            ("ixTokenLedger", Address.ADDRESS_SZ + sizeof(long), opts),
            ("ixTransactionId", SHA256Hash.HASH_SZ, opts),
            ("ixTransactionAddress", Address.ADDRESS_SZ + sizeof(long), opts),
            ("ixScheduledTransaction", Address.ADDRESS_SZ + sizeof(long), opts)
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

    public bool Exists(string ixName, byte[] key)
    {
        return Database.HasKey(ixName, key);
    }

    public byte[]? Get(string ixName, byte[] key)
    {
        var bytes = Database.Get(ixName, key);

        if (bytes.Length == 0)
        {
            return null;
        }
        
        return bytes;
    }

    public T? Get<T>(string ixName, byte[] key)
    {
        byte[] result = Database.Get(ixName, key);

        if (result.Length == 0)
        {
            return default;
        }

        return MemoryPackSerializer.Deserialize<T>(result);
    }

    public byte[][] GetMany(string ixName, byte[][] keys)
    {
        return Database.GetMany(ixName, keys);
    }

    public List<T> GetMany<T>(string ixName, byte[][] keys)
    {
        var results = Database.GetMany(ixName, keys);
        var items = new List<T>(results.Length);

        foreach (var result in results)
        {
            var data = MemoryPackSerializer.Deserialize<T>(result);

            if (data is not null)
            {
                items.Add(data);
            }
        }

        return items;
    }

    public void Put(string ixName, ReadOnlySpan<byte> key, byte[] bytes, ITransaction? transaction = null)
    {
        if (transaction is not null)
        {
            transaction.GetConnection().Put(ixName, key, bytes);
            return;
        }

        Database.Put(ixName, key, bytes);
    }

    public void Put<T>(string ixName, ReadOnlySpan<byte> key, T entity, ITransaction? transaction = null)
    {
        var bytes = MemoryPackSerializer.Serialize<T>(entity);

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

    public T? FindFirst<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
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

        return MemoryPackSerializer.Deserialize<T>(iterator.Value());
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

    public List<T> FindAll<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
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
            var data = MemoryPackSerializer.Deserialize<T>(iterator.Value());

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

    public List<T> FindLast<T>(string ixName, int count)
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToLast();

        var results = new List<T>(Math.Max(count, 0));

        while (iterator.Valid())
        {
            var data = MemoryPackSerializer.Deserialize<T>(iterator.Value());

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
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToLast();

        if (!iterator.Valid())
        {
            return default(T);
        }

        return MemoryPackSerializer.Deserialize<T>(iterator.Value());
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

    public T? FindLast<T>(string ixName, ReadOnlySpan<byte> keyPrefix)
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
            return default(T);
        }

        return MemoryPackSerializer.Deserialize<T>(iterator.Value());
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

    public List<T> GetAll<T>(string ixName)
    {
        using var readOptions = new ReadOptions();
        using var iterator = Database.CreateIterator(ixName, readOptions);

        iterator.SeekToFirst();

        var results = new List<T>();

        while (iterator.Valid())
        {
            var data = MemoryPackSerializer.Deserialize<T>(iterator.Value());

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

    public List<T> GetRange<T>(string ixName, int count, int toSkip)
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

            var data = MemoryPackSerializer.Deserialize<T>(iterator.Value());

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
        Database.RecreateColumnFamily("Block");
        Database.RecreateColumnFamily("View");
        Database.RecreateColumnFamily("Vote");
        Database.RecreateColumnFamily("ChainState");
        Database.RecreateColumnFamily("Ledger");
        Database.RecreateColumnFamily("Transaction");
        Database.RecreateColumnFamily("Contract");
        Database.RecreateColumnFamily("ContractCode");
        Database.RecreateColumnFamily("ContractSnapshot");
        Database.RecreateColumnFamily("Token");
        Database.RecreateColumnFamily("Validator");
        Database.RecreateColumnFamily("ixViewHash");
        Database.RecreateColumnFamily("ixTokenId");
        Database.RecreateColumnFamily("ixTokenLedger");
        Database.RecreateColumnFamily("ixTransactionNum");
        Database.RecreateColumnFamily("ixTransactionAddress");
        Database.RecreateColumnFamily("ixScheduledTransaction");
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
