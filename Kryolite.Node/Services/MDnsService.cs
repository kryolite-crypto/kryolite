using System.Net;
using Makaretu.Dns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class MDnsService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<MDnsService> _logger;
    private readonly MulticastService _mdns;
    private readonly ServiceDiscovery _serviceDiscovery;

    public MDnsService(IConfiguration configuration, ILogger<MDnsService> logger)
    {
        _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        MulticastService.IncludeLoopbackInterfaces = true;

        _mdns = new MulticastService
        {
            IgnoreDuplicateMessages = false
        };

        _serviceDiscovery = new ServiceDiscovery(_mdns);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() =>
    {
        try
        {
            var nameBytes = Guid.NewGuid().ToString()
                    .Split('-')
                    .First()
                    .ToList();
            var name = string.Join(string.Empty, nameBytes);

            var bind = _config.GetValue<string>("bind");
            var port = _config.GetValue<ushort>("port");

            var rpc = new ServiceProfile($"{name}-rpc", "_rpc._kryolite._tcp.", port, bind == "0.0.0.0" ? null : [IPAddress.Parse(bind!)]);
            _serviceDiscovery.Advertise(rpc);

            var jsonbind = _config.GetValue<string?>("rpcbind");
            var jsonportstr = _config.GetValue<string?>("rpcport");

            if (!string.IsNullOrEmpty(jsonbind) && ushort.TryParse(jsonportstr, out var jsonport))
            {
                var json = new ServiceProfile($"{name}-json", "_json._kryolite._tcp.", jsonport, jsonbind == "0.0.0.0" ? null : [IPAddress.Parse(jsonbind!)]);
                _serviceDiscovery.Advertise(json);
            }

            _mdns.Start();
            _logger.LogInformation("mDNS          [UP]");
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting mDNS services");
        }
    });

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _mdns.Stop();

            _serviceDiscovery.Dispose();
            _mdns.Dispose();

            _logger.LogInformation("mDNS          [DOWN]");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping mDNS services");
        }

        return base.StopAsync(cancellationToken);
    }
}
