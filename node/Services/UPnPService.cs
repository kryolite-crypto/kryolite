using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Open.Nat;
using Kryolite.Shared;

namespace Kryolite.Node;

public class UPnPService : BackgroundService
{
    private readonly IServer server;
    private readonly IConfiguration configuration;
    private readonly ILogger<UPnPService> logger;
    private readonly StartupSequence startup;
    private List<Mapping> mappings = new();

    public UPnPService(IServer server, IConfiguration configuration, ILogger<UPnPService> logger, StartupSequence startup)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = configuration.GetValue<bool>("EnableUPNP");

            if (!enabled)
            {
                logger.LogInformation("UPnP          [DISABLED]");
                return;
            }

            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();
            var ports = addresses
                .Where(x => x is not null)
                .Select(x => new Uri(x))
                .Where(x => !x.IsLoopback)
                .Select(x => x.Port)
                .Distinct();

            if (ports.Count() == 0)
            {
                logger.LogInformation("No external http(s) endpoints configured, skipping UPNP discovery...");
                return;
            }

            logger.LogInformation("UPnP enabled, performing NAT discovery");

            var discoverer = new NatDiscoverer();

            var cts = new CancellationTokenSource(5000);
            var devices = await discoverer.DiscoverDevicesAsync(PortMapper.Upnp | PortMapper.Pmp, cts);

            foreach (var device in devices)
            {
                logger.LogInformation($"UPnP: External IP = {await device.GetExternalIPAsync()}");

                foreach (var port in ports)
                {
                    logger.LogInformation($"UPnP: Mapping port TCP {port}:{port}");

                    var mapping = new Mapping(Protocol.Tcp, port, port);
                    await device.CreatePortMapAsync(mapping);

                    mappings.Add(mapping);
                }
            }
            logger.LogInformation("UPnP          [UP]");
        }
        catch (TaskCanceledException)
        {

        }
        catch (MappingException mEx)
        {
            logger.LogError($"UPnP mapping error: {mEx.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error with UPnP discovery");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (mappings.Count() == 0)
        {
            return;
        }

        try
        {
            var discoverer = new NatDiscoverer();

            var cts = new CancellationTokenSource(10000);
            var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            
            foreach(var mapping in mappings)
            {
                try
                {
                    await device.DeletePortMapAsync(mapping);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Failed to delete UPnP mapping {mapping.PublicPort}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error removing UPnP mappings");
        }

        await base.StopAsync(cancellationToken);
    }
}
