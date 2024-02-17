using System.CommandLine;
using System.Text.Json;
using Kryolite.Node;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Cli;

public static class WalletCmd
{
    public static Command Build(IConfiguration configuration)
    {
        var walletCmd = new Command("wallet", "Manage wallets");
        var createCmd = new Command("create", "Create new wallet");
        var listCmd = new Command("list", "List wallets");

        var outputOpt = new Option<string>("--output", "Output format json|default");
        outputOpt.AddAlias("-o");

        walletCmd.AddCommand(createCmd);
        walletCmd.AddCommand(listCmd);
        walletCmd.AddGlobalOption(outputOpt);

        createCmd.SetHandler((output) =>
        {
            var walletRepository = new WalletRepository(configuration);
            var wallet = Wallet.Create();

            walletRepository.Add(wallet);

            switch (output)
            {
                case "json":
                    /*var json = JsonSerializer.Serialize(new {
                        Address = wallet.Address,
                        PrivateKey = wallet.PrivateKey.ToString(),
                        PublicKey = wallet.PublicKey.ToString()
                    });

                    Console.WriteLine(json);*/
                    throw new NotImplementedException();
                default:
                    Console.WriteLine(wallet.Address);
                    break;
            }
        }, outputOpt);

        listCmd.SetHandler((output) => 
        {
            var walletRepository = new WalletRepository(configuration);
            var wallets = walletRepository.GetWallets();

            switch (output)
            {
                case "json":
                    /*var json = JsonSerializer.Serialize(wallets.Select(x => new {
                            Address = x.Value.Address,
                            PrivateKey = x.Value.PrivateKey.ToString(),
                            PublicKey = x.Value.PublicKey.ToString()
                        }
                    ).ToList(), Program.serializerOpts);

                    Console.WriteLine(json);*/
                    throw new NotImplementedException();
                default:
                    foreach (var wallet in wallets)
                    {
                        Console.WriteLine(wallet.Value.Address);
                    }
                    break;
            }
        }, outputOpt);

        return walletCmd;
    }
}
