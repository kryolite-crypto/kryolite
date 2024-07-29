
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Kryolite.Module.Upnp.Tests;

public class UpnpServiceExtensionsTests
{
    [Fact]
    public void AddUpnpService_ShouldRegisterCorrectServices()
    {
        var sc = new Mock<IServiceCollection>();

        sc.Object.AddUpnpModule();

        Assert.Contains("ICollection<ServiceDescriptor>.Add(ServiceType: Kryolite.Module.Upnp.IDiscoverer Lifetime: Singleton ImplementationType: Kryolite.Module.Upnp.Discoverer)", 
            sc.Invocations.Select(x => x.ToString()));

        Assert.Contains("ICollection<ServiceDescriptor>.Add(ServiceType: Microsoft.Extensions.Hosting.IHostedService Lifetime: Singleton ImplementationType: Kryolite.Module.Upnp.UpnpService)", 
            sc.Invocations.Select(x => x.ToString()));
    }
}