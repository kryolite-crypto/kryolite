using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Module.SmartContract;

public static class SmartContractExtensions
{
    public static IServiceCollection AddSmartcontracts(this IServiceCollection services)
    {
        services.AddSingleton<ICache, Cache>();
        services.AddSingleton<IVirtualMachineFactory, VirtualMachineFactory>();
        return services;
    }
}