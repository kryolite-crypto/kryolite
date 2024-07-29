using System.Net;
using Kryolite.Module.Mdns;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class MDnsService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<MDnsService> _logger;
    private MdnsServer? _mdns;

    public MDnsService(IConfiguration configuration, ILogger<MDnsService> logger)
    {
        _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() =>
    {
        try
        {
            var bind = _config.GetValue<string>("bind");
            var port = _config.GetValue<ushort>("port");

            _mdns = new MdnsServer(Constant.MDNS_SERVICE_NAME, IPAddress.Parse(bind!), port);
            _mdns.StartListening(stoppingToken);

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
            _mdns?.Dispose();
            _logger.LogInformation("mDNS          [DOWN]");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping mDNS services");
        }

        return base.StopAsync(cancellationToken);
    }
}
