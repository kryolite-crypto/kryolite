using Marccacoin.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class BlockchainService : BackgroundService
{
    public static string DATA_DIR { get; private set; } = "data";
    private readonly StartupSequence startup;

    public BlockchainService(IBlockchainManager blockchainManager, StartupSequence startup, ILogger<BlockchainService> logger, IConfiguration configuration) {
        BlockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        DATA_DIR = configuration.GetValue<string>("data-dir") ?? "data";
    }

    private IBlockchainManager BlockchainManager { get; }
    private ILogger<BlockchainService> Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.IsNullOrEmpty(DATA_DIR)) {
            Logger.LogInformation($"Redirecting data directory to {DATA_DIR}");
        }

        Directory.CreateDirectory(DATA_DIR);

        if (BlockchainManager.GetCurrentHeight() == 0) {
            InitializeGenesisBlock();
        }

        Logger.LogInformation("Blockchain \t\x1B[1m\x1B[32m[UP][TESTNET]\x1B[39m\x1B[22m");
        startup.Blockchain.Set();
        await Task.CompletedTask;
    }

    private void InitializeGenesisBlock()
    {
        var genesis = new Block {
            Id = 0,
            Header = new BlockHeader {
                ParentHash = new SHA256Hash(),
                Timestamp = new DateTimeOffset(1917, 12, 6, 0, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                Nonce = new Nonce { Buffer = new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }},
                Difficulty = new Difficulty { Value = 0 }
            }
        };

        if(!BlockchainManager.AddBlock(genesis)) {
            Logger.LogError("Failed to initialize Genesis");
        }
    }
}