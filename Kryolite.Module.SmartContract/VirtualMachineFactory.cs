using Kryolite.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.SmartContract;

public class VirtualMachineFactory : IVirtualMachineFactory
{
    private ICache _cache;
    private IServiceProvider _provider;
    private ILoggerFactory _loggerFactory;

    public VirtualMachineFactory(ICache cache, IServiceProvider provider, ILoggerFactory loggerFactory)
    {
        _cache = cache;
        _provider = provider;
        _loggerFactory = loggerFactory;
    }

    public VirtualMachine Create(IContext context, ReadOnlySpan<byte> code)
    {
        return new VirtualMachine(code, context, _loggerFactory.CreateLogger<VirtualMachine>());
    }

    public VirtualMachine Load(IContext context)
    {
        var address = context.Contract.Address;

        if (_cache.TryGetValue(address, out var vm))
        {
            return vm.WithContext(context);
        }

        using var scope = _provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStoreRepository>();

        var code = repository.GetContractCode(address) ?? throw new Exception("contract code not found from db");
        var snapshot = repository.GetLatestSnapshot(address) ?? throw new Exception("contract snapshot not found from db");
        var logger = _loggerFactory.CreateLogger<VirtualMachine>();

        vm = _cache.Set(address, new(code, context, logger));
        vm.RestoreSnapshot(snapshot);

        return vm;
    }
}
