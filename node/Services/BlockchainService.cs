using System.Text;
using System.Text.Unicode;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class BlockchainService : BackgroundService
{
    public static string DATA_PATH = string.Empty;

    private readonly StartupSequence startup;
    private readonly IConfiguration configuration;

    public BlockchainService(IBlockchainManager blockchainManager, StartupSequence startup, ILogger<BlockchainService> logger, IConfiguration configuration) {
        BlockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // TODO: build argument?? or configuration property?
        var bytes = Encoding.UTF8.GetBytes(configuration.GetValue<string?>("NetworkName") ?? "MAINNET");
        Array.Resize(ref bytes, 32);

        GenesisSeed = new SHA256Hash
        {
            Buffer = bytes
        };
    }

    private IBlockchainManager BlockchainManager { get; }
    private ILogger<BlockchainService> Logger { get; }

    private SHA256Hash GenesisSeed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var genesis = BlockchainManager.GetPosBlock(0);

        if (genesis == null) 
        {
            InitializeGenesisBlock();
        }

        if (genesis != null && genesis.ParentHash != GenesisSeed)
        {
            Logger.LogInformation("Blockchain Seed has changed, resetting chain...");
            BlockchainManager.ResetChain();
            InitializeGenesisBlock();
        }

        Logger.LogInformation($"Blockchain    \x1B[1m\x1B[32m[UP]\x1B[34m[{configuration.GetValue<string?>("NetworkName") ?? "MAINNET"}]\x1B[39m\x1B[22m");
        startup.Blockchain.Set();
        await Task.CompletedTask;
    }

    private void InitializeGenesisBlock()
    {
        var pow = new PowBlock {
            Height = 0,
            ParentHash = GenesisSeed,
            Timestamp = new DateTimeOffset(1917, 12, 6, 0, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            Nonce = new Nonce { Buffer = new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }},
            Difficulty = new Difficulty { Value = 0 }
        };

        var pos = new PosBlock {
            Height = 0,
            ParentHash = GenesisSeed,
            Timestamp = new DateTimeOffset(1917, 12, 6, 0, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            Pow = pow,
            SignedBy = new PublicKey { Buffer = new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }},
            Signature = new Signature { Buffer = new byte[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0  }}
        };

        if(!BlockchainManager.AddBlock(pos, false, false)) {
            Logger.LogError("Failed to initialize Genesis");
        }
    }
}