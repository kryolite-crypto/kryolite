using System.Collections;
using System.Globalization;
using System.Numerics;

namespace Marccacoin.Shared;
    
public static class Extensions
{
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