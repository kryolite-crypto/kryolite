﻿using System.CommandLine;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Kryolite.Node;
using Kryolite.Shared;
using MessagePack;

public class Program
{
    private static JsonSerializerOptions serializerOpts = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private static async Task<int> Main(string[] args)
    {
        serializerOpts.PropertyNameCaseInsensitive = true;
        serializerOpts.Converters.Add(new AddressConverter());
        serializerOpts.Converters.Add(new NonceConverter());
        serializerOpts.Converters.Add(new PrivateKeyConverter());
        serializerOpts.Converters.Add(new PublicKeyConverter());
        serializerOpts.Converters.Add(new SHA256HashConverter());
        serializerOpts.Converters.Add(new SignatureConverter());
        serializerOpts.Converters.Add(new DifficultyConverter());

        PacketFormatter.Register<CallMethod>(Packet.CallMethod);
        PacketFormatter.Register<NewContract>(Packet.NewContract);

        var dataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        Directory.CreateDirectory(dataDir);

        BlockchainService.DATA_PATH = dataDir;

        var rootCmd = new RootCommand("Kryolite CLI");

        var nodeOption = new Option<string?>(name: "--node", description: "Node url");
        rootCmd.AddGlobalOption(nodeOption);

        rootCmd.Add(BuildWalletCommand());
        rootCmd.Add(BuildSendCommand(nodeOption));
        rootCmd.Add(BuildContractCommand(nodeOption));

        return await rootCmd.InvokeAsync(args);
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
            var walletRepository = new WalletRepository();

            var wallet = new Wallet
            {
                Type = WalletType.WALLET
            };

            walletRepository.Add(wallet);

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
            var walletRepository = new WalletRepository();
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

    private static Command BuildSendCommand(Option<string?> nodeOption)
    {
        var sendCmd = new Command("send", "Send funds / assets to address");

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
            IsRequired = false
        };

        var contractMethodOption = new Option<string>(name: "--contract-method", description: "Contract method name to execute")
        {
            IsRequired = false
        };

        var contractParamsOption = new Option<string>(name: "--contract-params", description: "Contract method name to execute: example '[\"foo\", 32, true]'")
        {
            IsRequired = false
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
        sendCmd.AddOption(contractMethodOption);
        sendCmd.AddOption(contractParamsOption);

        sendCmd.SetHandler(async (from, to, amount, node, contractMethod, contractParams) =>
        {
            var walletRepository = new WalletRepository();
            var wallets = walletRepository.GetWallets();

            node = node ?? await ZeroConf.DiscoverNodeAsync();

            if(!wallets.TryGetValue(from, out var wallet))
            {
                Console.WriteLine("Wallet not found from wallet.dat");
                Console.WriteLine("'" + from + "'");
                Environment.Exit(-1);
            }

            TransactionPayload? transactionPayload = null;

            if (!string.IsNullOrEmpty(contractMethod))
            {
                transactionPayload = new TransactionPayload
                {
                    Payload = new CallMethod
                    {
                        Method = contractMethod,
                        Params = string.IsNullOrEmpty(contractParams) ? null : JsonSerializer.Deserialize<object[]>(contractParams, serializerOpts)
                    }
                };
            }

            var lz4Options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithOmitAssemblyVersion(true);

            var tx = new Transaction
            {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = wallet.PublicKey,
                To = to,
                Value = (ulong)(amount * 1000000),
                MaxFee = 1,
                Nonce = 69,
                Data = transactionPayload != null ? MessagePackSerializer.Serialize(transactionPayload, lz4Options) : null
            };

            tx.Sign(wallet.PrivateKey);

            var json = JsonSerializer.Serialize(tx, serializerOpts);
            Console.WriteLine(json);
            var stringContent = new StringContent(json, UnicodeEncoding.UTF8, "application/json");

            using var http = new HttpClient();
            await http.PostAsync($"{node}/tx", stringContent);

            Console.WriteLine($"Transaction sent to {node}");
        }, fromOption, toOption, amountOption, nodeOption, contractMethodOption, contractParamsOption);

        return sendCmd;
    }

    private static Command BuildContractCommand(Option<string?> nodeOption)
    {
        var contractCmd = new Command("contract", "Manage Contracts");
        var uploadCmd = new Command("upload", "Upload contract");

        var nameOption = new Argument<string>(name: "NAME", description: "Contract name")
        {

        };

        var fromOption = new Argument<string>(name: "ADDRESS", description: "Address to send contract from (Contract Owner)")
        {
            
        };

        var fileOption = new Argument<string>(name: "PATH", description: "Path to WASM file to upload.")
        {
            
        };

        uploadCmd.AddArgument(fileOption);
        uploadCmd.AddArgument(fromOption);
        uploadCmd.AddArgument(nameOption);

        contractCmd.AddCommand(uploadCmd);

        uploadCmd.SetHandler(async (name, from, file, node) =>
        {
            var walletRepository = new WalletRepository();
            var wallets = walletRepository.GetWallets();

            node = node ?? await ZeroConf.DiscoverNodeAsync();

            if(!wallets.TryGetValue(from, out var wallet))
            {
                Console.WriteLine("Wallet not found from wallet.dat");
                Console.WriteLine("'" + from + "'");
                Environment.Exit(-1);
            }

            if(!File.Exists(file)) 
            {
                Console.WriteLine($"File does not exist {file}");
            }

            var bytes = File.ReadAllBytes(file);

            var contract = new Contract(
                wallet.PublicKey.ToAddress(),
                name,
                bytes
            );

            Console.WriteLine(contract.ToAddress());

            var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
            var newContract = new NewContract(name, bytes);

            var payload = new TransactionPayload
            {
                Payload = newContract
            };

            var tx = new Transaction
            {
                TransactionType = TransactionType.CONTRACT,
                PublicKey = wallet.PublicKey,
                To = contract.ToAddress(),
                Value = 0,
                Data = MessagePackSerializer.Serialize(payload, lz4Options),
                MaxFee = 1,
                Nonce = 69
            };

            tx.Sign(wallet.PrivateKey);
            
            var json = JsonSerializer.Serialize(tx, serializerOpts);
            var stringContent = new StringContent(json, UnicodeEncoding.UTF8, "application/json");

            using var http = new HttpClient();
            await http.PostAsync($"{node}/tx", stringContent);

            Console.WriteLine($"Contract sent to {node}");
        }, nameOption, fromOption, fileOption, nodeOption);

        return contractCmd;
    }
}
