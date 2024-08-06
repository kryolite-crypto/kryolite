using Kryolite.Interface;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.Validator;

internal class Runner : IRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Runner> _logger;

    private readonly PublicKey _nodeKey;
    private TaskCompletionSource _enabled = new();

    public bool Completed => _enabled.Task.IsCompleted;
    public bool Canceled => _enabled.Task.IsCanceled;

    public Runner(IServiceProvider serviceProvider, IKeyRepository keyRepository, ILogger<Runner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _nodeKey = keyRepository.GetPublicKey();
    }

    public async Task Execute(CancellationToken stoppingToken)
    {
        try
        {
            var synchronizer = _serviceProvider.GetRequiredService<ISynchronizer>();

            while (!stoppingToken.IsCancellationRequested)
            {
                await _enabled.Task;
                await synchronizer.WaitForNextWindow(stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
                var slots = scope.ServiceProvider.GetRequiredService<ISlots>();

                if (!slots.TryGetNextLeader(out var nextLeader))
                {
                    _logger.LogWarning("Leader selection could not determine next leader, assigning self");
                    nextLeader.PublicKey = _nodeKey;
                }

                if (nextLeader.PublicKey == _nodeKey)
                {
                    _logger.LogInformation("Preparing a view to publish as current leader");

                    var generator = scope.ServiceProvider.GetRequiredService<IGenerator>();
                    generator.GenerateView();
                }
                else
                {
                    _logger.LogInformation("Leader is {publicKey}. Waiting to receive view...", nextLeader.PublicKey.ToAddress());
                }

                if (!await synchronizer.WaitForView(nextLeader.Height, stoppingToken))
                {
                    _logger.LogInformation("Leader {publicKey} failed to create view", nextLeader.PublicKey.ToAddress());
                    slots.Ban(nextLeader.PublicKey);
                    continue;
                }

                slots.Clear();
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("ValidatorRunner got cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ValidatorRunner failed");
        }
    }

    public void Enable()
    {
        _enabled.TrySetResult();
    }

    public void Disable()
    {
        _enabled.TrySetCanceled();
        _enabled = new();
    }
}
