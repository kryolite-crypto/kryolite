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

    public static Lock EnterReadLockEx(this ReaderWriterLockSlim rwlock)
    {
        return new Lock(rwlock, false);
    }

    public static Lock EnterWriteLockEx(this ReaderWriterLockSlim rwlock)
    {
        return new Lock(rwlock, true);
    }

    public readonly ref struct Lock
    {
        private readonly ReaderWriterLockSlim _rwlock;
        private readonly bool _write;

        public Lock(ReaderWriterLockSlim rwlock, bool write)
        {
            _rwlock = rwlock;
            _write = write;

            if (_write)
            {
                rwlock.EnterWriteLock();
            }
            else
            {
                rwlock.EnterReadLock();
            }
        }

        public void Dispose()
        {
            if (_write)
            {
                _rwlock.ExitWriteLock();
            }
            else
            {
                _rwlock.ExitReadLock();
            }
        }
    }
}