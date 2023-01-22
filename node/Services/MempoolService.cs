using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class MempoolService : BackgroundService
{
    private readonly StartupSequence startup;

    public MempoolService(StartupSequence startup, ILogger<MempoolService> logger)
    {
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<MempoolService> Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.Blockchain.WaitOneAsync();
        Logger.LogInformation("Mempool \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        startup.Mempool.Set();
        await Task.CompletedTask;
    }
}