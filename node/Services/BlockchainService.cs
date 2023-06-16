using System.Text;
using System.Text.Unicode;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class BlockchainService : BackgroundService
{
    public static string DATA_PATH = string.Empty;
    private SHA256Hash GenesisSeed;

    public IServiceProvider ServiceProvider { get; }
    public StartupSequence Startup { get; }
    private ILogger<BlockchainService> Logger { get; }
    public IConfiguration Configuration { get; }

    public BlockchainService(IServiceProvider serviceProvider, StartupSequence startup, ILogger<BlockchainService> logger, IConfiguration configuration) {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Startup = startup ?? throw new ArgumentNullException(nameof(startup));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // TODO: build argument?? or configuration property?
        var bytes = Encoding.UTF8.GetBytes(configuration.GetValue<string?>("NetworkName") ?? "MAINNET");
        Array.Resize(ref bytes, 32);

        GenesisSeed = new SHA256Hash(bytes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = ServiceProvider.CreateScope();
            var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

            var genesis = blockchainManager.GetGenesis();

            if (genesis is null)
            {
                InitializeGenesisBlock(blockchainManager);
            }

            if (genesis != null && !Enumerable.SequenceEqual(genesis.Pow ?? new byte[0], GenesisSeed.Buffer))
            {
                Logger.LogInformation("Blockchain Seed has changed, resetting chain...");
                blockchainManager.ResetChain();
                InitializeGenesisBlock(blockchainManager);
            }

            Logger.LogInformation($"Blockchain    [UP][{Configuration.GetValue<string?>("NetworkName") ?? "MAINNET"}]");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "BlockchainService error");
        }
    }

    private void InitializeGenesisBlock(IStoreManager blockchainManager)
    {
        var timestamp = new DateTimeOffset(2023, 1, 1, 0, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var genesis = new Genesis {
            TransactionType = TransactionType.GENESIS,
            Data = Encoding.UTF8.GetBytes(Configuration.GetValue<string?>("NetworkName") ?? "MAINNET"),
            Pow = GenesisSeed,
            Timestamp = timestamp,
            Height = 0,
            PublicKey = new PublicKey(),
            Signature = new Signature()
        };

        genesis.Parents.Add(new SHA256Hash());
        genesis.Parents.Add(new SHA256Hash());

        genesis.TransactionId = genesis.CalculateHash();

        if (!blockchainManager.AddGenesis(genesis))
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
            Signature = new Signature()
        };

        view.Parents.Add(genesis.TransactionId);
        view.Parents.Add(new SHA256Hash());

        view.TransactionId = view.CalculateHash();

        blockchainManager.AddView(view, false);
    }
}
