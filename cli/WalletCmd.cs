using System.CommandLine;
using Kryolite.Wallet;
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
            var account = walletRepository.CreateAccount();

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
                    Console.WriteLine(account.Address);
                    break;
            }
        }, outputOpt);

        listCmd.SetHandler((output) => 
        {
            var walletRepository = new WalletRepository(configuration);

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
                    foreach (var wallet in walletRepository.GetAccounts())
                    {
                        Console.WriteLine(wallet.Value.Address);
                    }
                    break;
            }
        }, outputOpt);

        return walletCmd;
    }
}
