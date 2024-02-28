using Kryolite.Node;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Builder;

namespace Kryolite.Daemon;

internal class Program
{
    static async Task Main(string[] args)
    {
        StartupLogoAndVersion.Print();

        var builder = WebApplication.CreateSlimBuilder(args)
            .BuildKryoliteNode(args);

        builder.Logging.AddCleanConsole();

        var app = builder.Build();

        app.UseNodeMiddleware();

        if (args.Contains("--test-rollback-rebuild"))
        {
            TestRollbackRebuild(app);
            return;
        }

        await app.StartAsync();
        await app.WaitForShutdownAsync();
    }

    private static void TestRollbackRebuild(IHost app)
    {
        using var scope = app.Services.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var height = storeManager.GetChainState().Id;

        Console.WriteLine($"Starting forced rollback - rebuild from height {0} to {height}");

        using (var checkpoint = storeManager.CreateCheckpoint())
        using (var staging = StagingManager.Open("staging", configuration))
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

            Console.WriteLine("Forced rollback rebuild done");
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
