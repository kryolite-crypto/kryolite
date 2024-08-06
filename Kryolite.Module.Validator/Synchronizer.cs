
using Kryolite.EventBus;
using Kryolite.Interface;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.Validator;

internal class Synchronizer : ISynchronizer
{
    private const long VIEW_INTERVAL_MS = Constant.VIEW_INTERVAL * 1000;
    private const long VIEW_INTERVAL_MIN_MS = VIEW_INTERVAL_MS / 2;
    
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Synchronizer> _logger;

    public Synchronizer(IServiceProvider serviceProvider, ILogger<Synchronizer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task WaitForNextWindow(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var firstTimestamp = storeManager.GetView(1)?.Timestamp ?? 0;
        var lastView = storeManager.GetLastView() ?? throw new Exception("selecting next view returned null");
        var height = lastView.Id + 1;

        var nextTimestamp = firstTimestamp + (VIEW_INTERVAL_MS * height);
        var minTimestamp = lastView.Timestamp + VIEW_INTERVAL_MIN_MS;
        var delay = Math.Max(nextTimestamp, minTimestamp) - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (delay <= 0)
        {
            _logger.LogInformation("Next view is due now");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Next view is due in {delay}ms", delay);

        return Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
    }

    public async Task<bool> WaitForView(long height, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var eventBus = _serviceProvider.GetRequiredService<IEventBus>();

        var tcs = new TaskCompletionSource<bool>(false);

        using var sub = eventBus.Subscribe<ChainState>(chainState =>
        {
            if (chainState.Id >= height)
            {
                tcs.SetResult(true);
            }
        });

        // Check if we already have the height we are trying to wait for.
        // Do this after subscribing to ChainState event to not lose any views.
        var lastView = storeManager.GetLastView();

        if (lastView is not null)
        {
            if (lastView.Id >= height)
            {
                tcs.SetResult(true);
            }
        }

        return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(VIEW_INTERVAL_MIN_MS), cancellationToken);
    }
}
