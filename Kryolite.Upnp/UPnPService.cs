using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Open.Nat;

namespace Kryolite.Upnp;

public class UpnpService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<UpnpService> _logger;
    private readonly IDiscoverer _discoverer;

    public UpnpService(IDiscoverer discoverer, IConfiguration configuration, ILogger<UpnpService> logger)
    {
        _discoverer = discoverer;
        _config = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _config.GetValue<bool>("upnp");

            if (!enabled)
            {
                _logger.LogInformation("Upnp          [DISABLED]");
                return;
            }

            var bind = _config.GetValue<string>("bind");
            var ip = IPAddress.Parse(bind!);

            if (IPAddress.IsLoopback(ip))
            {
                _logger.LogInformation("Disabling Upnp for localhost binding");
                _logger.LogInformation("Upnp          [DISABLED]");
                return;
            }

            var port = _config.GetValue<ushort>("port");

            var timeoutSource = new CancellationTokenSource(5000);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var devices = await _discoverer.DiscoverDevicesAsync(PortMapper.Upnp | PortMapper.Pmp, cts);

            foreach (var device in devices)
            {
                try
                {
                    _logger.LogDebug("Upnp: Mapping port TCP {port}:{port}", port, port);
                    await _discoverer.CreatePortMapAsync(device, port);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Failed to map {device} {message}:", device.HostEndPoint, ex.ToString());
                }
            }

            _logger.LogInformation("Upnp          [UP]");
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Upnp discovery did not find any devices");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _logger.LogError("Error with Upnp discovery: {error}", ex.Message);
            _logger.LogInformation("Upnp          [FAILED]");
        }

    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _discoverer.ReleaseAll();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error removing Upnp mappings");
        }

        _logger.LogInformation("Upnp          [DOWN]");
        await base.StopAsync(cancellationToken);
    }
}
