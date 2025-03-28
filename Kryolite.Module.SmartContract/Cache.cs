using System.Diagnostics.CodeAnalysis;
using Kryolite.Type;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Module.SmartContract;

public class Cache : ICache
{
    private readonly MemoryCache _vmCache;

    private readonly int _cacheExpiration;

    public Cache(IConfiguration configuration)
    {
        _cacheExpiration = configuration.GetValue<int>("kryovm.cache.expiration");

        var cacheSize = configuration.GetValue<int>("kryovm.cache.size");

        _vmCache = new(new MemoryCacheOptions
        {
            SizeLimit = cacheSize * 1024 * 1024
        });
    }

    public bool TryGetValue(Address address, [NotNullWhen(true)] out VirtualMachine? vm)
        => _vmCache.TryGetValue(address, out vm);

    public VirtualMachine Set(Address address, VirtualMachine vm)
    {
        var opts = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_cacheExpiration),
            Size = vm.Size,
        };

        opts.RegisterPostEvictionCallback(new PostEvictionDelegate(
            (_, value, _, _) => ((VirtualMachine?)value)?.Dispose()
        ));

        return _vmCache.Set(address, vm, opts);
    }
}
