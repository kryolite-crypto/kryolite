using System.Collections.Concurrent;
using System.Reactive;
using System.Threading.Tasks.Dataflow;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Redbus.Interfaces;

namespace Kryolite.Node;

public class ValidatorService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<ValidatorService> logger;
    private readonly StartupSequence startup;

    private Wallet Node { get; set; }
    private ManualResetEventSlim AllowExecution { get; set; } = new(true);
    private IEventBus EventBus { get; }

    public ValidatorService(IServiceProvider serviceProvider, IKeyRepository keyRepository, IEventBus eventBus, ILogger<ValidatorService> logger, StartupSequence startup)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));

        Node = keyRepository.GetKey();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Run(() => startup.Application.Wait(stoppingToken));

            var task = StartValidator(stoppingToken);

            if (Constant.SEED_VALIDATORS.Contains(Node.PublicKey.ToAddress()))
            {
                AllowExecution.Set();
            }
            else
            {
                EventBus.Subscribe<Ledger>(ledger => {
                    if (ledger.Address != Node.Address)
                    {
                        return;
                    }

                    var isEnabled = AllowExecution.IsSet;

                    if (isEnabled && ledger.Balance < Constant.MIN_STAKE)
                    {
                        AllowExecution.Reset();
                    }

                    if (!isEnabled && ledger.Balance >= Constant.MIN_STAKE)
                    {
                        AllowExecution.Set();
                    }
                });

                using var scope = serviceProvider.CreateScope();

                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

                // TODO: Repository.IsVerifier
                if (storeManager.GetLedger(Node.Address)?.Balance >= Constant.MIN_STAKE)
                {
                    AllowExecution.Set();
                }
            }

            await task;
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ValidatorService error");
        }
    }

    ConcurrentBag<PublicKey> Banned = new();

    private async Task StartValidator(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Run(() => AllowExecution.Wait(stoppingToken));
            logger.LogInformation("Validator     [ACTIVE]");

            await SynchronizeViewGenerator(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = serviceProvider.CreateScope();

                var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

                var lastView = blockchainManager.GetLastView() ?? throw new Exception("LastView returned null");

                var votes = blockchainManager.GetVotesAtHeight(lastView.Height ?? 0);
                var nextLeader = votes
                    .Where(x => !Banned.Contains(x.PublicKey))
                    .MinBy(x => x.Signature)?.PublicKey;

                logger.LogInformation("View #{} received {} votes", lastView.Height, votes.Count);

                if (nextLeader is null)
                {
                    logger.LogWarning("Leader selection could not determine next leader, assigning self");
                    nextLeader = Node.PublicKey;
                }

                logger.LogInformation("Next leader is {publicKey}", nextLeader);

                if (nextLeader == Node.PublicKey)
                {
                    GenerateView(blockchainManager, lastView);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                var nextView = blockchainManager.GetLastView() ?? throw new Exception("selecting next view returned null");

                if (nextView.TransactionId == lastView.TransactionId)
                {
                    logger.LogInformation("Leader {publicKey} failed to create view", nextLeader);
                    Banned.Add(nextLeader);
                    continue;
                }

                Banned.Clear();

                if(!AllowExecution.IsSet)
                {
                    logger.LogInformation("Validator     [INACTIVE]");
                    await Task.Run(() => AllowExecution.Wait(stoppingToken));
                    logger.LogInformation("Validator     [ACTIVE]");

                    nextView = blockchainManager.GetLastView() ?? throw new Exception("selecting next view returned null");
                }

                await SynchronizeViewGenerator(nextView, stoppingToken);
            }
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ValidatorThread error");
        }
    }

    private void GenerateView(IStoreManager blockchainManager, View lastView)
    {
        var height = (lastView?.Height ?? 0) + 1L;
        var nextView = new View(Node.PublicKey, height, blockchainManager.GetTransactionToValidate());
        
        nextView.Sign(Node.PrivateKey);

        blockchainManager.AddView(nextView, true, true);
    }

    private async Task SynchronizeViewGenerator(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        logger.LogInformation($"Synchronize view generator");
        var lastView = blockchainManager.GetLastView() ?? throw new Exception("view not initialized");

        var nextHeartbeat = lastView.Timestamp + 60_000;
        var syncPeriod = nextHeartbeat - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (syncPeriod < 0)
        {
            syncPeriod = 0;
        }

        await Task.Delay((int)syncPeriod, stoppingToken);
    }

    private async Task SynchronizeViewGenerator(View lastView, CancellationToken stoppingToken)
    {
        var nextHeartbeat = lastView.Timestamp + 60_000;
        var syncPeriod = nextHeartbeat - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (syncPeriod < 0)
        {
            syncPeriod = 0;
        }

        await Task.Delay((int)syncPeriod, stoppingToken);
    }
}
