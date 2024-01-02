using Microsoft.Extensions.Caching.Memory;

namespace Kryolite.Shared;
    
public static class Extensions
{
    public static string ToHexString(this PrivateKey bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static string ToHexString(this PublicKey bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    public static IDisposable EnterReadLockEx(this ReaderWriterLockSlim rwlock)
    {
        return new ReadLock(rwlock);
    }

    public static IDisposable EnterWriteLockEx(this ReaderWriterLockSlim rwlock)
    {
        return new WriteLock(rwlock);
    }

    public readonly struct ReadLock : IDisposable
    {
        private readonly ReaderWriterLockSlim rwlock;

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

    public readonly struct WriteLock : IDisposable
    {
        private readonly ReaderWriterLockSlim rwlock;

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