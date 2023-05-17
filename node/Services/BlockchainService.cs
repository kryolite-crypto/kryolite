using System.Text;
using System.Text.Unicode;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
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

        GenesisSeed = new SHA256Hash(bytes);
    }

    private IBlockchainManager BlockchainManager { get; }
    private ILogger<BlockchainService> Logger { get; }

    private SHA256Hash GenesisSeed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            //var genesis = BlockchainManager.GetPosBlock(0);

            //if (genesis) 
            //{
                InitializeGenesisBlock();
            //}

            /*if (genesis != null && genesis.Pow != GenesisSeed)
            {
                Logger.LogInformation("Blockchain Seed has changed, resetting chain...");
                BlockchainManager.ResetChain();
                InitializeGenesisBlock();
            }*/

            Logger.LogInformation($"Blockchain    [UP][{configuration.GetValue<string?>("NetworkName") ?? "MAINNET"}]");
            startup.Blockchain.Set();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "BlockchainService error");
        }
    }

    private void InitializeGenesisBlock()
    {
        var timestamp = new DateTimeOffset(2023, 1, 1, 0, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var genesis = new Genesis {
            NetworkName = configuration.GetValue<string?>("NetworkName") ?? "MAINNET",
            Pow = GenesisSeed,
            Timestamp = timestamp,
            PublicKey = new PublicKey(),
            Signature = new Signature(),            
            Validates = new List<Transaction>
            {
                new Transaction { Timestamp = timestamp, Value = Constant.COLLATERAL, PublicKey = "aSDj8ob2rbAkiqLb5WLVYd6J5U5vDupMTgLgqCpDCs6f", Signature = "5sWSVrgswgV1P3xY9vKtgw1DbBx6sUSSQA1L3C7bqH7YNJcxmxaPaQ9RUxjMTuu9j3Cer4SMQ6arypLv7uFPW3L2" }
            }
        };

        foreach (var validates in genesis.Validates)
        {
            validates.TransactionId = validates.CalculateHash();
        }

        if(!BlockchainManager.AddGenesis(genesis))
        {
            Logger.LogError("Failed to initialize Genesis");
        }

        var view = new View
        {
            TransactionType = TransactionType.VIEW,
            Value = 0,
            Data = BitConverter.GetBytes(0L),
            Timestamp = timestamp,
            Height = 0,
            PublicKey = new PublicKey(),
            Signature = new Signature(),
            Validates = BlockchainManager.GetTransactionToValidate()
        };

        view.TransactionId = view.CalculateHash();

        var vote = new Vote
        {
            TransactionId = view.TransactionId,
            PublicKey = "aSDj8ob2rbAkiqLb5WLVYd6J5U5vDupMTgLgqCpDCs6f",
            Signature = "5sWSVrgswgV1P3xY9vKtgw1DbBx6sUSSQA1L3C7bqH7YNJcxmxaPaQ9RUxjMTuu9j3Cer4SMQ6arypLv7uFPW3L2"
        };

        view.Votes.Add(vote);

        BlockchainManager.AddView(view);
    }
}
