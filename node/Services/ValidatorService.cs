using System.Collections.Concurrent;
using System.Collections.Immutable;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class ValidatorService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<ValidatorService> logger;
    private readonly StartupSequence startup;

    private Wallet Node { get; set; }
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
                await task;
                return;
            }

            EventBus.Subscribe<ValidatorEnable>(validator => {
                if (validator.Address != Node.Address)
                {
                    return;
                }

                logger.LogInformation("Validator     [ACTIVE]");
            });

            EventBus.Subscribe<ValidatorDisable>(validator => {
                if (validator.Address != Node.Address)
                {
                    return;
                }

                logger.LogInformation("Validator     [INACTIVE]");
            });

            using var scope = serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStoreRepository>();

            if (repository.IsValidator(Node.Address))
            {
                logger.LogInformation("Validator     [ACTIVE]");
            }
            else
            {
                logger.LogInformation("Validator     [INACTIVE]");
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
            await SynchronizeViewGenerator(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = serviceProvider.CreateScope();
                var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
                var meshNetwork = scope.ServiceProvider.GetRequiredService<IMeshNetwork>();

                var lastView = blockchainManager.GetLastView() ?? throw new Exception("LastView returned null");
                
                var height = lastView.Id - 1; // offset height by one since votes are confirmed at (height % Constant.VOTE_INTERVAL + 1)
                var slotNumber = (int)(height % Constant.VOTE_INTERVAL);
                var voteHeight = lastView.Id - slotNumber;
                var votes = blockchainManager.GetVotesAtHeight(voteHeight);
                
                logger.LogDebug("Loading votes from height {voteHeight} (id = {id}, slotNumber = {slotNumber}, voteCount = {voteCount})",
                    voteHeight,
                    lastView.Id,
                    slotNumber,
                    votes.Count
                );

                PublicKey? nextLeader = null;
                
                if (votes.Count > 0)
                {
                    var voters = votes
                        .Where(x => !Banned.Contains(x.PublicKey))
                        .OrderBy(x => x.Signature)
                        .Select(x => x.PublicKey)
                        .ToList();

                    if (voters.Count > 0)
                    {
                        nextLeader = voters[slotNumber % voters.Count];
                    }
                }

                if (slotNumber == 0)
                {
                    logger.LogInformation("View #{id} received {count} votes", lastView.Id, votes.Count);
                }

                if (nextLeader is null)
                {
                    logger.LogWarning("Leader selection could not determine next leader, assigning self");
                    nextLeader = Node.PublicKey;

                    // TODO: We should have some kind of vote who will create next view
                    await Task.Delay(Random.Shared.Next(10000));
                }

                logger.LogInformation("Next leader is {publicKey}", nextLeader.ToAddress());

                if (nextLeader == Node.PublicKey)
                {
                    GenerateView(blockchainManager, lastView);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                var nextView = blockchainManager.GetLastView() ?? throw new Exception("selecting next view returned null");

                if (nextView.Id == lastView.Id)
                {
                    // maybe we were disconnected during view broadcast, try to request it from peers
                    await meshNetwork.BroadcastAsync(new ViewRequestById(lastView.Id + 1, true));

                    logger.LogInformation("Leader {publicKey} failed to create view", nextLeader.ToAddress());
                    Banned.Add(nextLeader);
                    continue;
                }

                Banned.Clear();

                nextView = blockchainManager.GetLastView() ?? throw new Exception("selecting next view returned null");

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
        var lastHash = lastView.GetHash() ?? SHA256Hash.NULL_HASH;

        var nextView = new View
        {
            Id = (lastView?.Id ?? 0) + 1L,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastHash = lastHash,
            PublicKey = Node.PublicKey,
            Blocks = blockchainManager.GetPendingBlocks() // TODO: we can get key values from dictionary
                .Where(x => x.LastHash == lastHash)
                .Select(x => x.GetHash())
                .ToList(),
            Votes = blockchainManager.GetPendingVotes() // TODO: we can get key values from dictionary
                .Where(x => x.ViewHash == lastHash)
                .Select(x => x.GetHash())
                .ToList(),
            Transactions = blockchainManager.GetPendingTransactions() // TODO: we can get key values from dictionary
                .Select(x => x.CalculateHash())
                .ToList()
        };
        
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
