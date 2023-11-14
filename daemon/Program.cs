using Kryolite.Node;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Reflection;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Node.Blockchain;
using Kryolite.EventBus;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Kryolite.Daemon;

internal class Program
{
    static async Task Main(string[] args)
    {
        var attr = Attribute.GetCustomAttribute(Assembly.GetEntryAssembly()!, typeof(AssemblyInformationalVersionAttribute)) 
            as AssemblyInformationalVersionAttribute;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($@"
 __                         .__  .__  __
|  | _________ ___.__. ____ |  | |__|/  |_  ____
|  |/ /\_  __ <   |  |/  _ \|  | |  \   __\/ __ \
|    <  |  | \/\___  (  <_> )  |_|  ||  | \  ___/
|__|_ \ |__|   / ____|\____/|____/__||__|  \___  >
     \/        \/                              \/
                            {attr?.InformationalVersion}
                                         ");
        Console.ForegroundColor = ConsoleColor.Gray;

        var config = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var dataDir = config.GetValue<string>("data-dir", defaultDataDir) ?? defaultDataDir;

        if (!Path.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        var versionPath = Path.Join(dataDir, $"store.version.{Constant.STORE_VERSION}");

        if (args.Contains("--resync") || !Path.Exists(versionPath))
        {
            Console.WriteLine("Performing full resync");
            var storeDir = Path.Join(dataDir, "store");

            if (Path.Exists(storeDir))
            {
                Directory.Delete(storeDir, true);
            }
        }

        if (args.Contains("--force-recreate"))
        {
            var renamedTarget = $"{dataDir}-{DateTimeOffset.Now:yyyyMMddhhmmss}";
            if (Path.Exists(dataDir))
            {
                Directory.Move(dataDir, renamedTarget);
                Console.WriteLine($"Rename {dataDir} to {renamedTarget}");
            }
        }

        Directory.CreateDirectory(dataDir);

        var walletRepository = new WalletRepository(config);
        walletRepository.Backup();

        var keyRepository = new KeyRepository(config);
        Console.WriteLine($"Server");
        Console.WriteLine($"\tPublic Key: {keyRepository.GetKey().PublicKey}");
        Console.WriteLine($"\tAddress: {keyRepository.GetKey().Address}");

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var app = WebHost.CreateDefaultBuilder()
            .ConfigureAppConfiguration((hostingContext, config) => config
                .AddJsonFile(configPath, optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "KRYOLITE__")
                .AddCommandLine(args))
            .ConfigureLogging(configure => configure.AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>())
            .UseStartup<Startup>()
            .Build();

        await app.StartAsync();

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var addresses = app.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

        foreach (var address in addresses)
        {
            logger.LogInformation($"Now listening on {address}");
        }

        app.Services.GetRequiredService<StartupSequence>()
            .Application.Set();

        if (args.Contains("--test-rollback-rebuild"))
        {
            TestRollbackRebuild(app);
        }

        await app.WaitForShutdownAsync();
    }

    private static void TestRollbackRebuild(IWebHost app)
    {
        using var scope = app.Services.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        var height = storeManager.GetChainState().Id;

        Console.WriteLine($"Starting forced rollback - rebuild from height {0} to {height}");

        using (var checkpoint = storeManager.CreateCheckpoint())
        using (var staging = StagingManager.Open("staging", configuration, loggerFactory))
        {
            staging.RollbackTo(0);

            for (var i = 1; i <= height; i++)
            {
                var view = storeManager.GetView(i);

                if (view is null)
                {
                    Console.WriteLine($"Failed to query view at {i}");
                    return;
                }

                var blocks = storeManager.GetBlocks(view.Blocks);
                var votes = storeManager.GetVotes(view.Votes);
                var transactions = storeManager.GetTransactions(view.Transactions);

                if (!staging.LoadBlocks(blocks))
                {
                    Console.WriteLine($"Failed to apply blocks at {i}");
                    return;
                }

                if (!staging.LoadVotes(votes))
                {
                    Console.WriteLine($"Failed to apply votes at {i}");
                    return;
                }

                if (!staging.LoadTransactions(transactions.Select(x => new TransactionDto(x)).ToList()))
                {
                    Console.WriteLine($"Failed to apply transactions at {i}");
                    return;
                }

                if (!staging.LoadView(view))
                {
                    Console.WriteLine($"Failed to apply view at {i}");
                    return;
                }
            }

            Console.WriteLine("Forced rollback rebuild was success");
            Console.WriteLine("Verifying ledger");

            var ledger = storeManager.GetRichList(1000);

            foreach (var wallet in ledger)
            {
                var other = staging.Repository.GetWallet(wallet.Address);

                if (wallet.Balance != other?.Balance)
                {
                    Console.WriteLine($"Balance mismatch on wallet {wallet.Address}");
                }
            }

            Console.WriteLine("Verifying validators");

            var validators = storeManager.GetValidators();

            foreach (var validator in validators)
            {
                var other = staging.Repository.GetStake(validator.NodeAddress);

                if (validator.Stake != other?.Stake)
                {
                    Console.WriteLine($"Stake mismatch on validator {validator.NodeAddress}");
                }

                if (validator.RewardAddress != other!.RewardAddress)
                {
                    Console.WriteLine($"RewardAddress mismatch on validator {validator.NodeAddress}");
                }
            }

            Console.WriteLine("Verification complete");
        }
    }
}
