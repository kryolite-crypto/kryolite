using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class DiscoveryService : BackgroundService
{
    public DiscoveryService(ILogger<DiscoveryService> logger) => Logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private ILogger<DiscoveryService> Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Discovery \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        await Task.CompletedTask;
    }
}