using System.Collections.Concurrent;
using System.Reactive;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class ValidatorService : BackgroundService
{
    private readonly IWalletManager walletManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly ILogger<ValidatorService> logger;
    private readonly StartupSequence startup;

    private bool Enabled { get; set; }
    private Wallet Node { get; set; }

    public ValidatorService(IWalletManager walletManager, IBlockchainManager blockchainManager, ILogger<ValidatorService> logger, StartupSequence startup)
    {
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() => startup.Blockchain.WaitOne());
        Node = walletManager.GetNodeWallet() ?? walletManager.CreateWallet(WalletType.VALIDATOR);

        blockchainManager.OnWalletUpdated(new ActionBlock<Wallet>(wallet => {
            // TODO: Create transaction to register as node
        }));

        InitValidatorChain();

        Enabled = true;
        logger.LogInformation("Validator     \x1B[32m[ACTIVE]\x1B[37m");
        
        await StartValidator(stoppingToken);
    }

    ConcurrentBag<PublicKey> Banned = new();

    private async Task StartValidator(CancellationToken stoppingToken)
    {
        await SynchronizeViewGenerator(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var lastView = blockchainManager.GetLastView() ?? throw new Exception("view not initialized");
            var nextLeader = lastView.Votes
                .Where(x => !Banned.Contains(x.PublicKey))
                .MinBy(x => x.Signature)?.PublicKey;

            logger.LogInformation("View #{viewId} received {} votes", lastView.Height, lastView.Votes.Count());

            if (nextLeader is null)
            {
                logger.LogWarning("Leader selection could not determine next leader, assigning self");
                nextLeader = Node.PublicKey;
            }

            logger.LogInformation("Next leader is {publicKey}", nextLeader);

            if (nextLeader == Node.PublicKey)
            {
                GenerateView(lastView);
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

            await SynchronizeViewGenerator(nextView, stoppingToken);
        }
    }

    private void GenerateView(View lastView)
    {
        var height = (lastView?.Height ?? 0) + 1L;
        var toValidate = blockchainManager.GetTransactionToValidate();

        var nextView = View.Create(Node.PublicKey, Node.PrivateKey, height);

        nextView.Validates.AddRange(toValidate);

        foreach (var tx in toValidate)
        {
            tx.ValidatedBy.Add(nextView);
        }

        // TODO: Asynchronously add to not skip execution
        blockchainManager.AddView(nextView);

        // broadcast lastHeartbeat.Signatures

        logger.LogInformation($"Generated view #{height}");
    }

    private async Task SynchronizeViewGenerator(CancellationToken stoppingToken)
    {
        logger.LogInformation($"Synchronize view generator");
        var lastView = blockchainManager.GetLastView() ?? throw new Exception("view not initialized");

        var nextHeartbeat = lastView.Timestamp + 60_000;
        var syncPeriod = nextHeartbeat - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await Task.Delay((int)syncPeriod, stoppingToken);
    }

    private async Task SynchronizeViewGenerator(View lastView, CancellationToken stoppingToken)
    {
        var nextHeartbeat = lastView.Timestamp + 60_000;
        var syncPeriod = nextHeartbeat - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await Task.Delay((int)syncPeriod, stoppingToken);
    }

    private void InitValidatorChain()
    {
        var view = blockchainManager.GetLastView();

        if (view is null)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var toValidate = blockchainManager.GetTransactionToValidate();

            view = new View
            {
                TransactionType = TransactionType.VIEW,
                Value = Constant.VALIDATOR_REWARD,
                Data = BitConverter.GetBytes(0L),
                Timestamp = timestamp,
                Height = 0,
                PublicKey = Node.PublicKey,
                Signature = new Signature(),
                Validates = toValidate
            };

            blockchainManager.AddView(view);
        }
    }
}
