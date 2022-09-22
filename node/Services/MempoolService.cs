using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marccacoin.Shared;

namespace Marccacoin;

public class MempoolService : BackgroundService
{
    public MempoolService(ILogger<MempoolService> logger) => Logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private ILogger<MempoolService> Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Mempool \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        await Task.CompletedTask;
    }
}