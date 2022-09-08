using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class SampoService : BackgroundService
{
    public SampoService(ILogger<SampoService> logger) => Logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private ILogger<SampoService> Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("SAMPO \t\x1B[90m[INACTIVE]\x1B[37m");
        await Task.CompletedTask;
    }
}