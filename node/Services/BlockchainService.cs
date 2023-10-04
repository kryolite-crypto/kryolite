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

        var bytes = Encoding.UTF8.GetBytes(Constant.NETWORK_NAME);
        Array.Resize(ref bytes, 32);

        GenesisSeed = new SHA256Hash(bytes);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = ServiceProvider.CreateScope();
            var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

            var genesis = blockchainManager.GetView(0L);

            if (genesis is null)
            {
                InitializeGenesisBlock((TransactionManager)blockchainManager, Logger);
            }

            if (genesis is not null && !Enumerable.SequenceEqual(genesis.LastHash.Buffer, GenesisSeed.Buffer))
            {
                Logger.LogInformation("Blockchain Seed has changed, resetting chain...");
                blockchainManager.ResetChain();
                InitializeGenesisBlock((TransactionManager)blockchainManager, Logger);
            }

            Logger.LogInformation($"Blockchain    [UP][{Configuration.GetValue<string?>("NetworkName") ?? "MAINNET"}]");

        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "BlockchainService error");
        }

        return Task.CompletedTask;
    }

    public static void InitializeGenesisBlock(TransactionManager blockchainManager, ILogger logger)
    {
        var bytes = Encoding.UTF8.GetBytes(Constant.NETWORK_NAME);
        Array.Resize(ref bytes, 32);

        var timestamp = new DateTimeOffset(2023, 1, 1, 0, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var genesis = new View {
            Id = 0,
            Timestamp = timestamp,
            LastHash = new SHA256Hash(bytes)
        };

        if (!blockchainManager.AddGenesis(genesis))
        {
            logger.LogError("Failed to initialize Genesis");
        }
    }
}
