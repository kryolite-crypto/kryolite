using Marccacoin.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class BlockchainService : BackgroundService
{
    public BlockchainService(IBlockchainManager blockchainManager, ILogger<BlockchainService> logger) {
        BlockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private IBlockchainManager BlockchainManager { get; }
    private ILogger<BlockchainService> Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (BlockchainManager.GetCurrentHeight() == 0) {
            InitializeGenesisBlock();
        }

        Logger.LogInformation("Blockchain \t\x1B[1m\x1B[32m[UP][TESTNET]\x1B[39m\x1B[22m");
        await Task.CompletedTask;
    }

    private void InitializeGenesisBlock()
    {
        var genesis = new Block {
            Id = 0,
            Header = new BlockHeader {
                ParentHash = new SHA256Hash(),
                Timestamp = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(),
                Nonce = new Nonce { Buffer = new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }},
                Difficulty = new Difficulty { Value = 0 }
            }
        };

        if(!BlockchainManager.AddBlock(genesis)) {
            Logger.LogError("Failed to initialize Genesis");
        }
    }
}