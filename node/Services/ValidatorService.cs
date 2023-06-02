using System.Collections.Concurrent;
using System.Reactive;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class ValidatorService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly IWalletManager walletManager;
    private readonly ILogger<ValidatorService> logger;
    private readonly StartupSequence startup;

    private Wallet Node { get; set; }
    private ManualResetEventSlim AllowExecution { get; set; } = new(true);

    public ValidatorService(IServiceProvider serviceProvider, IWalletManager walletManager, ILogger<ValidatorService> logger, StartupSequence startup)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));

        Node = walletManager.GetNodeWallet() ?? walletManager.CreateWallet(WalletType.VALIDATOR);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Run(() => startup.Application.Wait(stoppingToken));

            var task = StartValidator(stoppingToken);

            /*blockchainManager.OnWalletUpdated(new ActionBlock<Wallet>(wallet => {
                if (wallet.WalletType != WalletType.VALIDATOR)
                {
                    return;
                }

                if (!AllowExecution.IsSet && wallet.Balance >= Constant.COLLATERAL)
                {
                    AllowExecution.Set();
                }

                if (AllowExecution.IsSet && wallet.Balance < Constant.COLLATERAL)
                {
                    AllowExecution.Reset();
                }
            }));*/

            if (Constant.SEED_VALIDATORS.Contains(Node.PublicKey))
            {
                AllowExecution.Set();
            }

            if (walletManager.GetNodeWallet()?.Balance >= Constant.COLLATERAL)
            {
                AllowExecution.Set();
            }

            /*use this to generate seed signatures
             * var v = new Vote
            {
                TransactionId = "5kxwZ17DyJWDwZypH1p9vRny93CyGjGF57Te78ebLuCN",
                PublicKey = "r9Lk4qxPGNZGBaLEjcWtAZmu8LRj5FQe7NCyRzNsnaX",
                Signature = "3GAvVpJcujfYEL4mMhZk6C4gpzWmDtuVxsww1Kw3iR8GqVpnnhbwfaGoZTspwsNwVpxro3CZfR9RuqGJvLmecbiZ"
            };

            v.Sign(Node.PrivateKey);

            logger.LogInformation(v.Signature.ToString());*/

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

                var blockchainManager = scope.ServiceProvider.GetRequiredService<IBlockchainManager>();

                var lastView = blockchainManager.GetLastView();
                var votes = blockchainManager.GetVotesAtHeight(lastView?.Height ?? 0);
                var nextLeader = votes
                    .Where(x => !Banned.Contains(x.PublicKey))
                    .MinBy(x => x.Signature)?.PublicKey;

                logger.LogInformation("View #{} received {} votes", lastView?.Height, votes.Count);

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

                    nextView = blockchainManager.GetLastView();
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

    private void GenerateView(IBlockchainManager blockchainManager, View lastView)
    {
        var height = (lastView?.Height ?? 0) + 1L;
        var nextView = new View(Node.PublicKey, height, blockchainManager.GetTransactionToValidate());

        nextView.Sign(Node.PrivateKey);

        // TODO: Asynchronously add to not skip execution
        if (blockchainManager.AddView(nextView, true))
        {
            var vote = new Vote(Node.PublicKey, nextView.Parents.Take(1).ToList(), nextView);
            vote.Sign(Node.PrivateKey);

            blockchainManager.AddVote(vote, true);
        }
    }

    private async Task SynchronizeViewGenerator(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IBlockchainManager>();

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
