
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Kryolite.Upnp.Tests;

public class UpnpServiceExtensionsTests
{
    [Fact]
    public void AddUpnpService_ShouldRegisterCorrectServices()
    {
        var sc = new Mock<IServiceCollection>();

        sc.Object.AddUpnpService();

        Assert.Contains("ICollection<ServiceDescriptor>.Add(ServiceType: Kryolite.Upnp.IDiscoverer Lifetime: Singleton ImplementationType: Kryolite.Upnp.Discoverer)", 
            sc.Invocations.Select(x => x.ToString()));

        Assert.Contains("ICollection<ServiceDescriptor>.Add(ServiceType: Microsoft.Extensions.Hosting.IHostedService Lifetime: Singleton ImplementationType: Kryolite.Upnp.UpnpService)", 
            sc.Invocations.Select(x => x.ToString()));
    }
}