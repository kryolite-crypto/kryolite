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

        InitHeartbeatChain();

        Enabled = true;
        logger.LogInformation("Validator     \x1B[32m[ACTIVE]\x1B[37m");
        
        await StartValidator(stoppingToken);
    }

    private async Task StartValidator(CancellationToken stoppingToken)
    {
        var lastHeartbeat = blockchainManager.GetLastHeartbeat() ?? throw new Exception("heartbeat not initialized");

        var nextHeartbeat = lastHeartbeat.Timestamp + 30_000;
        var syncPeriod = nextHeartbeat - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await Task.Delay((int)syncPeriod, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            lastHeartbeat = blockchainManager.GetLastHeartbeat() ?? throw new Exception("heartbeat not initialized");

            var nextLeader = lastHeartbeat.Signatures.MinBy(x => x.Signature);

            var height = lastHeartbeat.Height + 1L;
            lastHeartbeat = Heartbeat.Create(Node.PublicKey, Node.PrivateKey, height);

            blockchainManager.AddHeartbeat(lastHeartbeat);
            // broadcast lastHeartbeat.Signatures

            logger.LogInformation($"Generated heartbeat for height {height}");
        }
    }

    private void InitHeartbeatChain()
    {
        var lastHeartbeat = blockchainManager.GetLastHeartbeat();

        if (lastHeartbeat is null)
        {
            var timestamp = DateTimeOffset.UtcNow;
            lastHeartbeat = new Heartbeat
            {
                TransactionType = TransactionType.HEARTBEAT,
                PublicKey = Node.PublicKey,
                Value = Constant.VALIDATOR_REWARD,
                Data = BitConverter.GetBytes(0L),
                Timestamp = timestamp.ToUnixTimeMilliseconds(),
                Height = 0
            };

            blockchainManager.AddHeartbeat(lastHeartbeat);
        }
    }
}
