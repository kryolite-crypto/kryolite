
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Open.Nat;

namespace Kryolite.Module.Upnp.Tests;

public class UpnpServiceTests
{
    [Fact]
    public async Task ExecuteAsync_DiscoverDevices_CreatePortMappingsWithCorrectPort()
    {
        var discoverer = new Mock<IDiscoverer>();
        var device1 = new Mock<NatDevice>();
        var device2 = new Mock<NatDevice>();

        discoverer.Setup(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()))
            .Returns(Task.FromResult<IEnumerable<NatDevice>>([device1.Object, device2.Object]));

        var logger = new Mock<ILogger<UpnpService>>();

        var config = new Mock<IConfiguration>();
        var upnp = new Mock<IConfigurationSection>();
        var bind = new Mock<IConfigurationSection>();
        var port = new Mock<IConfigurationSection>();

        upnp.Setup(x => x.Value).Returns("true");
        bind.Setup(x => x.Value).Returns("0.0.0.0");
        port.Setup(x => x.Value).Returns("42");
        config.Setup(x => x.GetSection("upnp")).Returns(upnp.Object);
        config.Setup(x => x.GetSection("bind")).Returns(bind.Object);
        config.Setup(x => x.GetSection("port")).Returns(port.Object);

        var service = new UpnpService(discoverer.Object, config.Object, logger.Object);
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // we should have invocation
        discoverer.Verify(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()), Times.Once());

        // and both devices should have incovation with correct port
        discoverer.Verify(x => x.CreatePortMapAsync(device1.Object, 42), Times.Once());
        discoverer.Verify(x => x.CreatePortMapAsync(device2.Object, 42), Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_DiscoverDevicesTimesOut()
    {
        var discoverer = new Mock<IDiscoverer>();
        var device1 = new Mock<NatDevice>();
        var device2 = new Mock<NatDevice>();

        discoverer.Setup(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()))
            .Throws(new TaskCanceledException());

        discoverer.Setup(x => x.CreatePortMapAsync(device1.Object, 42)).ThrowsAsync(new Exception("error"));

        var logger = new Mock<ILogger<UpnpService>>();

        var config = new Mock<IConfiguration>();
        var upnp = new Mock<IConfigurationSection>();
        var bind = new Mock<IConfigurationSection>();
        var port = new Mock<IConfigurationSection>();

        upnp.Setup(x => x.Value).Returns("true");
        bind.Setup(x => x.Value).Returns("0.0.0.0");
        port.Setup(x => x.Value).Returns("42");
        config.Setup(x => x.GetSection("upnp")).Returns(upnp.Object);
        config.Setup(x => x.GetSection("bind")).Returns(bind.Object);
        config.Setup(x => x.GetSection("port")).Returns(port.Object);

        var service = new UpnpService(discoverer.Object, config.Object, logger.Object);
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // we should have invocation
        discoverer.Verify(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()), Times.Once());

        // but should not have any port mappings due to timeout
        discoverer.Verify(x => x.CreatePortMapAsync(device1.Object, 42), Times.Never());
        discoverer.Verify(x => x.CreatePortMapAsync(device2.Object, 42), Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_DisableOnLocalhost_Ipv4()
    {
        var discoverer = new Mock<IDiscoverer>();
        var device1 = new Mock<NatDevice>();
        var device2 = new Mock<NatDevice>();

        discoverer.Setup(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()))
            .Throws(new TaskCanceledException());

        discoverer.Setup(x => x.CreatePortMapAsync(device1.Object, 42)).ThrowsAsync(new Exception("error"));

        var logger = new Mock<ILogger<UpnpService>>();

        var config = new Mock<IConfiguration>();
        var upnp = new Mock<IConfigurationSection>();
        var bind = new Mock<IConfigurationSection>();
        var port = new Mock<IConfigurationSection>();

        upnp.Setup(x => x.Value).Returns("true");
        bind.Setup(x => x.Value).Returns("127.0.0.1");
        port.Setup(x => x.Value).Returns("42");
        config.Setup(x => x.GetSection("upnp")).Returns(upnp.Object);
        config.Setup(x => x.GetSection("bind")).Returns(bind.Object);
        config.Setup(x => x.GetSection("port")).Returns(port.Object);

        var service = new UpnpService(discoverer.Object, config.Object, logger.Object);
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // we should have invocation
        discoverer.Verify(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()), Times.Never());

        // but should not have any port mappings due to timeout
        discoverer.Verify(x => x.CreatePortMapAsync(device1.Object, 42), Times.Never());
        discoverer.Verify(x => x.CreatePortMapAsync(device2.Object, 42), Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_DisableOnLocalhost_Ipv6()
    {
        var discoverer = new Mock<IDiscoverer>();
        var device1 = new Mock<NatDevice>();
        var device2 = new Mock<NatDevice>();

        discoverer.Setup(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()))
            .Throws(new TaskCanceledException());

        discoverer.Setup(x => x.CreatePortMapAsync(device1.Object, 42)).ThrowsAsync(new Exception("error"));

        var logger = new Mock<ILogger<UpnpService>>();

        var config = new Mock<IConfiguration>();
        var upnp = new Mock<IConfigurationSection>();
        var bind = new Mock<IConfigurationSection>();
        var port = new Mock<IConfigurationSection>();

        upnp.Setup(x => x.Value).Returns("true");
        bind.Setup(x => x.Value).Returns("::1");
        port.Setup(x => x.Value).Returns("42");
        config.Setup(x => x.GetSection("upnp")).Returns(upnp.Object);
        config.Setup(x => x.GetSection("bind")).Returns(bind.Object);
        config.Setup(x => x.GetSection("port")).Returns(port.Object);

        var service = new UpnpService(discoverer.Object, config.Object, logger.Object);
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // we should not have any invocations
        discoverer.Verify(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()), Times.Never());

        // but should not have any port mappings due to timeout
        discoverer.Verify(x => x.CreatePortMapAsync(device1.Object, 42), Times.Never());
        discoverer.Verify(x => x.CreatePortMapAsync(device2.Object, 42), Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_FirstPortMappingThrowsException_SecondStillMaps()
    {
        var discoverer = new Mock<IDiscoverer>();
        var device1 = new Mock<NatDevice>();
        var device2 = new Mock<NatDevice>();

        discoverer.Setup(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()))
            .Returns(Task.FromResult<IEnumerable<NatDevice>>([device1.Object, device2.Object]));

        discoverer.Setup(x => x.CreatePortMapAsync(device1.Object, 42)).ThrowsAsync(new Exception("error"));

        var logger = new Mock<ILogger<UpnpService>>();

        var config = new Mock<IConfiguration>();
        var upnp = new Mock<IConfigurationSection>();
        var bind = new Mock<IConfigurationSection>();
        var port = new Mock<IConfigurationSection>();

        upnp.Setup(x => x.Value).Returns("true");
        bind.Setup(x => x.Value).Returns("0.0.0.0");
        port.Setup(x => x.Value).Returns("42");
        config.Setup(x => x.GetSection("upnp")).Returns(upnp.Object);
        config.Setup(x => x.GetSection("bind")).Returns(bind.Object);
        config.Setup(x => x.GetSection("port")).Returns(port.Object);

        var service = new UpnpService(discoverer.Object, config.Object, logger.Object);
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // we should have invocation
        discoverer.Verify(x => x.DiscoverDevicesAsync(It.IsAny<PortMapper>(), It.IsAny<CancellationTokenSource>()), Times.Once());

        // and both devices should have incovation with correct port
        discoverer.Verify(x => x.CreatePortMapAsync(device1.Object, 42), Times.Once());
        discoverer.Verify(x => x.CreatePortMapAsync(device2.Object, 42), Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBeDisabledByConfig()
    {
        var discoverer = new Mock<IDiscoverer>();
        var logger = new Mock<ILogger<UpnpService>>();
        var config = new Mock<IConfiguration>();
        var upnp = new Mock<IConfigurationSection>();
        var bind = new Mock<IConfigurationSection>();
        var port = new Mock<IConfigurationSection>();

        upnp.Setup(x => x.Value).Returns("false");
        bind.Setup(x => x.Value).Returns("0.0.0.0");
        config.Setup(x => x.GetSection("upnp")).Returns(upnp.Object);
        config.Setup(x => x.GetSection("bind")).Returns(bind.Object);
        config.Setup(x => x.GetSection("port")).Returns(port.Object);

        var service = new UpnpService(discoverer.Object, config.Object, logger.Object);
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Cannot mock extension methods, check from invocations list
        Assert.Contains("ILogger.Log<FormattedLogValues>(LogLevel.Information, 0, Upnp          [DISABLED], null, Func<FormattedLogValues, Exception, string>)",
            logger.Invocations.Select(x => x.ToString()));

        // Should not be any incvocations to discoverer
        Assert.Empty(discoverer.Invocations);
    }
}