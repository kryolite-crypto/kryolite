using Kryolite.Node;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Builder;
using Kryolite.Shared;
using Kryolite.Node.Storage.Key;

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

        Console.WriteLine($"Starting rollback - rebuild from height {0} to {height}");

        using (var checkpoint = storeManager.CreateCheckpoint())
        using (var staging = StagingManager.Open("staging", configuration))
        {
            staging.RollbackTo(0);

            Console.WriteLine("Rollback completed");
            Console.WriteLine("Starting rebuild");

            foreach (var ledger2 in staging.Repository.GetRichList(1000))
            {
                if (ledger2.Balance != 0)
                {
                    Console.WriteLine($"Ledger balance not reset after rollback: {ledger2.Address}: {ledger2.Balance}");
                    return;
                }
            }

            var vals = staging.GetValidators();

            if (vals.Count != Constant.SEED_VALIDATORS.Length)
            {
                Console.WriteLine($"{vals.Count} validators exists after rollback");
                return;
            }

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

                if (blocks.Count != view.Blocks.Count)
                {
                    Console.WriteLine($"Height {view.Id} is missing {view.Blocks.Count - blocks.Count} blocks");
                    return;
                }

                if (votes.Count != view.Votes.Count)
                {
                    Console.WriteLine($"Height {view.Id} is missing {view.Votes.Count - votes.Count} votes");
                    return;
                }

                if (transactions.Count != view.Transactions.Count)
                {
                    Console.WriteLine($"Height {view.Id} is missing {view.Transactions.Count - transactions.Count} transactions");
                    return;
                }

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

            Console.WriteLine("Rebuild completed");
            Console.WriteLine("Verifying ledger");

            var ledger = storeManager.GetRichList(1000);

            foreach (var wallet in ledger)
            {
                var other = staging.Repository.GetWallet(wallet.Address);

                if (wallet.Balance != other?.Balance)
                {
                    Console.WriteLine($"Balance mismatch on wallet {wallet.Address}, {wallet.Balance} != {other?.Balance}");
                }
            }

            Console.WriteLine("Verifying validators");

            var validators = storeManager.GetValidators();

            foreach (var validator in validators)
            {
                var other = staging.Repository.GetValidator(validator.NodeAddress);

                if (validator.Stake != other?.Stake)
                {
                    Console.WriteLine($"Stake mismatch on validator {validator.NodeAddress}, {validator.Stake} != {other?.Stake}");
                }

                if (validator.RewardAddress != other!.RewardAddress)
                {
                    Console.WriteLine($"RewardAddress mismatch on validator {validator.NodeAddress}");
                }
            }

            Console.WriteLine("Verifying contracts");

            var contracts = storeManager.GetContracts();

            foreach (var contract in contracts)
            {
                var other = staging.Repository.GetContract(contract.Address);

                if (contract.Name != other?.Name)
                {
                    Console.WriteLine($"Contract name mismatch, {contract.Name} != {other?.Name}");
                }

                if (contract.Address != other?.Address!)
                {
                    Console.WriteLine($"Contract addres mismatch, {contract.Address} != {other?.Address}");
                }

                if (contract.Owner != other?.Owner!)
                {
                    Console.WriteLine($"Contract owner mismatch, {contract.Owner} != {other?.Owner}");
                }

                var code1 = storeManager.GetContractCode(contract.Address);
                var code2 = staging.Repository.GetContractCode(contract.Address);

                if (!Enumerable.SequenceEqual(code1!, code2!))
                {
                    Console.WriteLine($"Contract code mismatch, {contract.Address}");
                }

                var ss1 = storeManager.GetContractSnapshot(contract.Address);
                var ss2 = staging.Repository.GetLatestSnapshot(contract.Address);

                if (!Enumerable.SequenceEqual(ss1!, ss2!))
                {
                    Console.WriteLine($"Contract snapshot mismatch, {contract.Address}");
                }
            }

            Console.WriteLine("Verification complete");
        }
    }
}
