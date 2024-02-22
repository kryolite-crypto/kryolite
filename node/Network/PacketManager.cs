using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class PacketManager : BackgroundService
{
    private static Channel<(Node, IBroadcast)> _channel = Channel.CreateUnbounded<(Node, IBroadcast)>();
    private readonly ILogger<PacketManager> _logger;
    private readonly IServiceProvider _serviceProvider;

    public PacketManager(ILogger<PacketManager> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PackMan       [UP]");

        try
        {
            await foreach (var (node, packet) in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                using var scope = _serviceProvider.CreateScope();
                await packet.Handle(node, scope.ServiceProvider);
            }
        }
        catch (OperationCanceledException)
        {
            // Do nothing, we're shutting down
        }

        _logger.LogInformation("PackMan       [DOWN]");
    }

    public static ValueTask Handle(Node node, IBroadcast packet, CancellationToken token)
    {
        return _channel.Writer.WriteAsync((node, packet), token);
    }
}