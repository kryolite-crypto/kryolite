using Kryolite.Interface;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.SmartContract;

public class VirtualMachineFactory : IVirtualMachineFactory
{
    private ICache _cache;
    private IStoreRepository _repository;
    private ILoggerFactory _loggerFactory;

    public VirtualMachineFactory(ICache cache, IStoreRepository repository, ILoggerFactory loggerFactory)
    {
        _cache = cache;
        _repository = repository;
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

        var code = _repository.GetContractCode(address) ?? throw new Exception("contract code not found from db");
        var snapshot = _repository.GetLatestSnapshot(address) ?? throw new Exception("contract snapshot not found from db");
        var logger = _loggerFactory.CreateLogger<VirtualMachine>();

        vm = _cache.Set(address, new(code, context, logger));
        vm.RestoreSnapshot(snapshot);

        return vm;
    }
}
