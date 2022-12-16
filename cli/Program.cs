// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using LiteDB;
using Marccacoin;
using Marccacoin.Shared;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

public class Program
{
    private static JsonSerializerOptions serializerOpts = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    private static async Task Main(string[] args)
    {
        InitializeLiteDb();

        var rootCmd = new RootCommand("Kryolite CLI");

        var nodeOption = new Option<string>(name: "--node", description: "Node url", getDefaultValue: () => "http://localhost:5000");
        rootCmd.AddGlobalOption(nodeOption);

        rootCmd.Add(BuildWalletCommand());
        rootCmd.Add(BuildSendCommand(nodeOption));

        await rootCmd.InvokeAsync(args);
    }

    private static Command BuildWalletCommand()
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
            using var walletRepository = new WalletRepository(true);

            var wallet = new Wallet
            {
                Type = WalletType.WALLET
            };

            walletRepository.Add(wallet);
            walletRepository.Commit();

            switch (output)
            {
                case "json":
                    var json = JsonSerializer.Serialize(new {
                        Address = wallet.Address,
                        PrivateKey = wallet.PrivateKey.ToHexString(),
                        PublicKey = wallet.PublicKey.ToHexString()
                    }, serializerOpts);

                    Console.WriteLine(json);
                    break;
                default:
                    Console.WriteLine(wallet.Address);
                    break;
            }
        }, outputOpt);

        listCmd.SetHandler((output) => 
        {
            using var walletRepository = new WalletRepository();
            var wallets = walletRepository.GetWallets();

            switch (output)
            {
                case "json":
                    var json = JsonSerializer.Serialize(wallets.Select(x => new {
                            Address = x.Value.Address,
                            PrivateKey = x.Value.PrivateKey.ToHexString(),
                            PublicKey = x.Value.PublicKey.ToHexString()
                        }
                    ).ToList(), serializerOpts);

                    Console.WriteLine(json);
                    break;
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

    private static Command BuildSendCommand(Option<string> nodeOption)
    {
        var sendCmd = new Command("send", "Send funds to address");

        var fromOption = new Option<string>(name: "--from", description: "Address to send from")
        {
            IsRequired = true
        };

        var toOption = new Option<string>(name: "--to", description: "Recipient Address")
        {
            IsRequired = true
        };

        var amountOption = new Option<decimal>(name: "--amount", description: "Amount to send")
        {
            IsRequired = true
        };

        sendCmd.AddValidator(result => 
        {
            if(!Address.IsValid(result.GetValueForOption(fromOption) ?? string.Empty))
            {
                result.ErrorMessage = "Invalid 'from' address";
            }

            if(!Address.IsValid(result.GetValueForOption(toOption) ?? string.Empty))
            {
                result.ErrorMessage = "Invalid 'to' address";
            }
        });

        sendCmd.AddOption(fromOption);
        sendCmd.AddOption(toOption);
        sendCmd.AddOption(amountOption);

        sendCmd.SetHandler(async (from, to, amount, node) =>
        {
            using var walletRepository = new WalletRepository();
            var wallets = walletRepository.GetWallets();

            if(!wallets.TryGetValue(from, out var wallet))
            {
                Console.WriteLine("Wallet not found from wallet.dat");
                Console.WriteLine("'" + from + "'");
                Environment.Exit(-1);
            }

            var tx = new Transaction
            {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = wallet.PublicKey,
                To = to,
                Value = (ulong)(amount * 1000000),
                MaxFee = 1,
                Nonce = 69
            };

            tx.Sign(wallet.PrivateKey);

            var json = JsonConvert.SerializeObject(tx);
            var stringContent = new StringContent(json, UnicodeEncoding.UTF8, "application/json");

            using var http = new HttpClient();
            await http.PostAsync($"{node}/tx", stringContent);

            Console.WriteLine($"Transaction sent to {node}");
        }, fromOption, toOption, amountOption, nodeOption);

        return sendCmd;
    }

    // TODO: Move this under node or shared project
    private static void InitializeLiteDb()
    {
        BsonMapper.Global.RegisterType<Difficulty>
        (
            serialize: (diff) => BitConverter.GetBytes(diff.Value),
            deserialize: (bson) => new Difficulty { Value = BitConverter.ToUInt32(bson.AsBinary) }
        );

        BsonMapper.Global.RegisterType<SHA256Hash>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Nonce>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Signature>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Address>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Marccacoin.Shared.PublicKey>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Marccacoin.Shared.PrivateKey>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<BigInteger>
        (
            serialize: (bigint) => bigint.ToByteArray(),
            deserialize: (bson) => new BigInteger(bson.AsBinary, true)
        );
    }
}