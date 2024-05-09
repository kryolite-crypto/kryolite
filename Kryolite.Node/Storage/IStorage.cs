
using Kryolite.ByteSerializer;
using Kryolite.RocksDb;

namespace Kryolite.Node.Storage;

public interface IStorage
{
    ulong GetCurrentKey();
    ulong NextKey(ITransaction? transaction = null);

    bool Exists(string ixName, byte[] key);

    byte[]? Get(string ixName, byte[] key);
    T? Get<T>(string ixName, byte[] key) where T : ISerializable, new();

    byte[][] GetMany(string ixName, byte[][] keys);
    List<T> GetMany<T>(string ixName, byte[][] keys) where T : ISerializable, new();

    void Put(string ixName, ReadOnlySpan<byte> key, byte[] bytes, ITransaction? transaction = null);
    void Put<T>(string ixName, ReadOnlySpan<byte> key, T entity, ITransaction? transaction = null) where T : ISerializable, new();
    void Delete(string ixName, ReadOnlySpan<byte> key, ITransaction? transaction = null);

    byte[]? FindFirst(string ixName, ReadOnlySpan<byte> keyPrefix);
    T? FindFirst<T>(string ixName, ReadOnlySpan<byte> keyPrefix) where T : ISerializable, new();

    List<byte[]> FindAll(string ixName);
    List<byte[]> FindAll(string ixName, ReadOnlySpan<byte> keyPrefix);
    List<T> FindAll<T>(string ixName, ReadOnlySpan<byte> keyPrefix) where T : ISerializable, new();

    T? FindLast<T>(string ixName) where T : ISerializable, new();
    byte[]? FindLast(string ixName);
    T? FindLast<T>(string ixName, ReadOnlySpan<byte> keyPrefix) where T : ISerializable, new();
    byte[]? FindLast(string ixName, ReadOnlySpan<byte> keyPrefix);
    List<byte[]> FindLast(string ixName, int count);
    List<T> FindLast<T>(string ixName, int count) where T : ISerializable, new();
    List<byte[]> FindLast(string ixName, ReadOnlySpan<byte> keyPrefix, int count);
    Iterator GetIterator(string ixName, ReadOptions readOpts);
    List<T> GetAll<T>(string ixName) where T : ISerializable, new();
    List<byte[]> GetRange(string ixName, int count, int toSkip);
    List<T> GetRange<T>(string ixName, int count, int toSkip) where T : ISerializable, new();
    void Reset();

    ITransaction BeginTransaction();
    RocksDb.RocksDb Open(string storePath);
    public void Compact();
    void Close();
    Checkpoint CreateCheckpoint();
}
