using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;

namespace Kryolite.Node;

public class MDNSService : BackgroundService
{
    private readonly IServer server;
    private readonly IConfiguration configuration;
    private readonly ILogger<UPnPService> logger;
    private readonly MulticastService mdns;
    private readonly ServiceDiscovery serviceDiscovery;
    private readonly TaskCompletionSource _source = new();

    public MDNSService(IServer server, IConfiguration configuration, ILogger<UPnPService> logger, IHostApplicationLifetime lifetime)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        MulticastService.IncludeLoopbackInterfaces = true;
        mdns = new MulticastService();
        serviceDiscovery = new ServiceDiscovery(mdns);

        lifetime.ApplicationStarted.Register(() => _source.SetResult());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _source.Task;
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

            var nameBytes = Guid.NewGuid().ToString()
                    .Split('-')
                    .First()
                    .ToList();

            var name = String.Join(String.Empty, nameBytes);

            var id = 0;
            foreach(var address in addresses)
            {
                var uri = new Uri(address);
                var ipAddress = IPAddress.Parse(uri.Host);

                var ipAddresses = new List<IPAddress>();

                if (IPAddress.Any.ToString() == ipAddress.ToString())
                {
                    ipAddresses = MulticastService.GetIPAddresses()
                        .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                        .ToList();
                }
                else if (IPAddress.IPv6Any.ToString() == ipAddress.ToString())
                {
                    ipAddresses = MulticastService.GetIPAddresses()
                        .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6)
                        .ToList();
                }
                else
                {
                    ipAddresses.Add(ipAddress);
                }

                var service = new ServiceProfile($"{name}-{id++}", "_kryolite._tcp.", (ushort)uri.Port, ipAddresses);

                serviceDiscovery.Advertise(service);
            }

            mdns.Start();

            logger.LogInformation("mDNS          [UP]");
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting mDNS services");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            mdns.Stop();

            serviceDiscovery.Dispose();
            mdns.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error stopping mDNS services");
        }

        return base.StopAsync(cancellationToken);
    }
}
