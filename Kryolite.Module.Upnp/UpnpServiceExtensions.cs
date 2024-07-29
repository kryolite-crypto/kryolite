using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Module.Upnp;

public static class UpnpServiceExtensions
{
    public static IServiceCollection AddUpnpModule(this IServiceCollection services)
    {
        services.AddSingleton<IDiscoverer, Discoverer>();
        services.AddHostedService<UpnpService>();
        return services;
    }
}
