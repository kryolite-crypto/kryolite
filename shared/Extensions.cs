using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Caching.Memory;

namespace Kryolite.Shared;
    
public static class Extensions
{
    public static void Buffer<T>(this BufferBlock<T> block, TimeSpan interval, Func<IList<T>, Task> action)
    {
        Task.Run(async () => 
        {
            using var timer = new PeriodicTimer(interval);

            while (!block.Completion.IsCompleted)
            {
                await timer.WaitForNextTickAsync();

                if (block.TryReceiveAll(out var list))
                {
                    if (list.Count > 0)
                    {
                        await action(list);
                    }
                }
            }
        });
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