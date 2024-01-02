using Microsoft.Extensions.Caching.Memory;

namespace Kryolite.Shared.Locks;
    
    public static class HashLockExtensions
    {
        public static IDisposable Lock(this SHA256Hash hash)
        {
            return new HashLock(hash);
        }

    }

    public readonly struct HashLock : IDisposable
    {
        private static readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly object _obj;

        public HashLock(SHA256Hash hash)
        {
            lock (_cache)
            {
                _obj = _cache.GetOrCreate(hash, (entry) => 
                {
                    entry.SetAbsoluteExpiration(DateTimeOffset.Now.AddMinutes(1));
                    return hash;
                })!;
            }

            Monitor.Enter(_obj);
        }

        public void Dispose()
        {
            if (Monitor.IsEntered(_obj))
            {
                Monitor.Exit(_obj);
            }
        }
    }
    