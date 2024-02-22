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
    private readonly IConfiguration _config;
    private readonly ILogger<UPnPService> _logger;
    private readonly NatDiscoverer _discoverer = new ();

    public UPnPService(IConfiguration configuration, ILogger<UPnPService> logger)
    {
        _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _config.GetValue<bool>("upnp");

            if (!enabled)
            {
                _logger.LogInformation("UPnP          [DISABLED]");
                return;
            }

            var bind = _config.GetValue<string>("bind");
            var port = _config.GetValue<ushort>("port");

            var cts = new CancellationTokenSource(1000);
            var devices = await _discoverer.DiscoverDevicesAsync(PortMapper.Upnp | PortMapper.Pmp, cts);

            foreach (var device in devices)
            {
                _logger.LogDebug("UPnP: Mapping port TCP {port}:{port}", port, port);

                var mapping = new Mapping(Protocol.Tcp, port, port);
                await device.CreatePortMapAsync(mapping);
            }

            _logger.LogInformation("UPnP          [UP]");
        }
        catch (TaskCanceledException)
        {

        }
        catch (MappingException mEx)
        {
            _logger.LogError("UPnP mapping error: {message}", mEx.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error with UPnP discovery");
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
            _logger.LogDebug(ex, "Error removing UPnP mappings");
        }

        await base.StopAsync(cancellationToken);
    }
}
