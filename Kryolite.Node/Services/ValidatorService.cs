using System.Collections.Concurrent;
using System.Collections.Immutable;
using Kryolite.EventBus;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Network;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class ValidatorService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<ValidatorService> logger;

    private PrivateKey SigningKey { get; set; }
    private PublicKey NodeKey { get; set; }
    private Address NodeAddress { get; set; }

    private IEventBus EventBus { get; }
    private readonly TaskCompletionSource _source = new();
    private TaskCompletionSource _enableValidator = new();

    public ValidatorService(IServiceProvider serviceProvider, IKeyRepository keyRepository, IEventBus eventBus, ILogger<ValidatorService> logger, IHostApplicationLifetime lifetime)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SigningKey = keyRepository.GetPrivateKey();
        NodeKey = keyRepository.GetPublicKey();
        NodeAddress = NodeKey.ToAddress();

        lifetime.ApplicationStarted.Register(() => _source.SetResult());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _source.Task;
            var task = StartValidator(stoppingToken);

            if (Constant.SEED_VALIDATORS.Contains(NodeKey.ToAddress()))
            {
                _enableValidator.SetResult();
                await task;
                return;
            }

            EventBus.Subscribe<ValidatorEnable>(validator => {
                if (validator.Address != NodeAddress)
                {
                    return;
                }

                _enableValidator.SetResult();
                logger.LogInformation("Validator     [ACTIVE]");
            });

            EventBus.Subscribe<ValidatorDisable>(validator => {
                if (validator.Address != NodeAddress)
                {
                    return;
                }

                _enableValidator = new();
                logger.LogInformation("Validator     [INACTIVE]");
            });

            using var scope = serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStoreRepository>();

            if (repository.IsValidator(NodeAddress))
            {
                _enableValidator.SetResult();
                logger.LogInformation("Validator     [ACTIVE]");
            }
            else
            {
                _enableValidator = new();
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
            await _enableValidator.Task.WaitAsync(stoppingToken);
            await SynchronizeViewGenerator(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = serviceProvider.CreateScope();
                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
                var connectionManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

                var lastView = storeManager.GetLastView() ?? throw new Exception("LastView returned null");
                
                var height = lastView.Id - 1; // offset height by one since votes are confirmed at (height % Constant.VOTE_INTERVAL + 1)
                var slotNumber = (int)(height % Constant.VOTE_INTERVAL);
                var voteHeight = lastView.Id - slotNumber;
                var votes = storeManager.GetVotesAtHeight(voteHeight);
                
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
                    nextLeader = NodeKey;

                    // TODO: We should have some kind of vote who will create next view
                    // this could create temporary chain splits, for now we use jitter
                    await Task.Delay(Random.Shared.Next(30000), stoppingToken);
                }

                logger.LogInformation("Next leader is {publicKey}", nextLeader.ToAddress());

                if (nextLeader == NodeKey)
                {
                    GenerateView(storeManager, lastView);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                var nextView = storeManager.GetLastView() ?? throw new Exception("selecting next view returned null");

                if (nextView.Id == lastView.Id)
                {
                    // We have not received new view, try to download it from current peers
                    var nodes = connectionManager.GetConnectedNodes();
                    var nextId = lastView.Id + 1;

                    foreach (var node in nodes)
                    {
                        try
                        {
                            var client = connectionManager.CreateClient(node);
                            var view = client.GetViewForId(nextId);

                            if (view is not null && storeManager.AddView(view, true, true))
                            {
                                goto gotview;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug("Validator failed to query view from {node}: {error}", node.Node.Uri, ex.Message);
                        }
                    }

                    logger.LogInformation("Leader {publicKey} failed to create view", nextLeader.ToAddress());
                    Banned.Add(nextLeader);
                    continue;
                }

gotview:

                Banned.Clear();

                await _enableValidator.Task.WaitAsync(stoppingToken);

                nextView = storeManager.GetLastView() ?? throw new Exception("selecting next view returned null");

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
            PublicKey = NodeKey,
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
        
        nextView.Sign(SigningKey);

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
