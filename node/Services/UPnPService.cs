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
    private readonly TaskCompletionSource _source = new();
    private readonly NatDiscoverer discoverer = new ();

    public UPnPService(IServer server, IConfiguration configuration, ILogger<UPnPService> logger, IHostApplicationLifetime lifetime)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        lifetime.ApplicationStarted.Register(() => _source.SetResult());
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _source.Task;

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

            if (!ports.Any())
            {
                logger.LogInformation("No external http(s) endpoints configured, skipping UPNP discovery...");
                return;
            }

            logger.LogDebug("UPnP enabled, performing NAT discovery");

            var cts = new CancellationTokenSource(1000);
            var devices = await discoverer.DiscoverDevicesAsync(PortMapper.Upnp | PortMapper.Pmp, cts);

            foreach (var device in devices)
            {
                logger.LogDebug("UPnP: External IP = {ip}", await device.GetExternalIPAsync());

                foreach (var port in ports)
                {
                    logger.LogDebug("UPnP: Mapping port TCP {port}:{port}", port, port);

                    var mapping = new Mapping(Protocol.Tcp, port, port);
                    await device.CreatePortMapAsync(mapping);
                }
            }
            logger.LogInformation("UPnP          [UP]");
        }
        catch (TaskCanceledException)
        {

        }
        catch (MappingException mEx)
        {
            logger.LogError("UPnP mapping error: {message}", mEx.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error with UPnP discovery");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            NatDiscoverer.ReleaseAll();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error removing UPnP mappings");
        }

        await base.StopAsync(cancellationToken);
    }
}
