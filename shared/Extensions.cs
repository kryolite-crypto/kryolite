

namespace Kryolite.Shared;
    
public static class Extensions
{
    public static string ToHexString(this byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static string ToHexString(this SHA256Hash bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static string ToHexString(this PrivateKey bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static string ToHexString(this PublicKey bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static string ToHexString(this Signature bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static IEnumerable<TElement> PeekTail<TElement, TPriority>(this PriorityQueue<TElement, TPriority> queue, int count) 
    {
        return queue.UnorderedItems.OrderByDescending(x => x.Priority).Take(count).Select(x => x.Element).ToList();
    }

    public static IDisposable EnterReadLockEx(this ReaderWriterLockSlim rwlock)
    {
        return new ReadLock(rwlock);
    }

    public static IDisposable EnterWriteLockEx(this ReaderWriterLockSlim rwlock)
    {
        return new WriteLock(rwlock);
    }

    public class ReadLock : IDisposable
    {
        private ReaderWriterLockSlim rwlock;

        public ReadLock(ReaderWriterLockSlim rwlock)
        {
            this.rwlock = rwlock ?? throw new ArgumentNullException(nameof(rwlock));
            rwlock.EnterReadLock();
        }

        public void Dispose()
        {
            rwlock.ExitReadLock();
        }
    }

    public class WriteLock : IDisposable
    {
        private ReaderWriterLockSlim rwlock;

        public WriteLock(ReaderWriterLockSlim rwlock)
        {
            this.rwlock = rwlock ?? throw new ArgumentNullException(nameof(rwlock));
            rwlock.EnterWriteLock();
        }

        public void Dispose()
        {
            rwlock.ExitWriteLock();
        }
    }
}