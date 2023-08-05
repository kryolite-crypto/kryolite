using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Storage;

public interface IStorage
{
    ulong GetCurrentKey();
    ulong NextKey(ITransaction? transaction = null);

    bool Exists(string ixName, ReadOnlySpan<byte> key);

    byte[]? Get(string ixName, byte[] key);
    T? Get<T>(string ixName, byte[] key);

    byte[][] GetMany(string ixName, byte[][] keys);
    List<T> GetMany<T>(string ixName, byte[][] keys);

    void Put(string ixName, ReadOnlySpan<byte> key, byte[] bytes, ITransaction? transaction = null);
    void Put<T>(string ixName, ReadOnlySpan<byte> key, T entity, ITransaction? transaction = null);
    void Delete(string ixName, ReadOnlySpan<byte> key, ITransaction? transaction = null);

    byte[]? FindFirst(string ixName, ReadOnlySpan<byte> keyPrefix);
    T? FindFirst<T>(string ixName, ReadOnlySpan<byte> keyPrefix);

    List<byte[]> FindAll(string ixName);
    List<byte[]> FindAll(string ixName, ReadOnlySpan<byte> keyPrefix);
    List<T> FindAll<T>(string ixName, ReadOnlySpan<byte> keyPrefix);

    T? FindLast<T>(string ixName, ReadOnlySpan<byte> keyPrefix);
    List<byte[]> FindLast(string ixName, int count);

    ITransaction BeginTransaction();
}
