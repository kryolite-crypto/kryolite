using Kryolite.EventBus;
using Kryolite.Interface;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.Validator;

internal class ValidatorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRunner _runner;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ValidatorService> _logger;
    
    private readonly PublicKey _nodeKey;
    private readonly TaskCompletionSource _startup = new();

    public ValidatorService(IServiceProvider serviceProvider, IRunner runner, IKeyRepository keyRepository, IEventBus eventBus, IHostApplicationLifetime lifetime, ILogger<ValidatorService> logger)
    {
        _serviceProvider = serviceProvider;
        _runner = runner;
        _eventBus = eventBus;
        _logger = logger;
        _nodeKey = keyRepository.GetPublicKey();

        lifetime.ApplicationStarted.Register(() => _startup.SetResult());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _startup.Task.WaitAsync(stoppingToken);
            var nodeAddress = _nodeKey.ToAddress();

            using var sub1 = _eventBus.Subscribe<ValidatorEnable>(validator => {
                if (validator.Address != nodeAddress)
                {
                    return;
                }

                Enable();
            });

            using var sub2 = _eventBus.Subscribe<ValidatorDisable>(validator => {
                if (validator.Address != nodeAddress)
                {
                    return;
                }

                Disable();
            });

            if (IsNodeValidator())
            {
                Enable();
            }
            else
            {
                Disable();
            }

            await _runner.Execute(stoppingToken);
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ValidatorService error");
        }
    }

    public bool IsNodeValidator()
    {
        var nodeAddress = _nodeKey.ToAddress();

        if (Constant.SEED_VALIDATORS.Contains(nodeAddress))
        {
            return true;
        }

        using var scope = _serviceProvider.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        return storeManager.IsValidator(nodeAddress);
    }

    public void Enable()
    {
        _runner.Enable();
        _logger.LogInformation("Validator     [ACTIVE]");
    }

    public void Disable()
    {
        _runner.Disable();
        _logger.LogInformation("Validator     [INACTIVE]");
    }
}
