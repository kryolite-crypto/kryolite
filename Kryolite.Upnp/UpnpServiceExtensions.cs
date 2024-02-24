using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Upnp;

public static class UpnpServiceExtensions
{
    public static IServiceCollection AddUpnpService(this IServiceCollection services)
    {
        services.AddSingleton<IDiscoverer, Discoverer>();
        services.AddHostedService<UpnpService>();
        return services;
    }
}
