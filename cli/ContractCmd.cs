using System.CommandLine;
using System.Text;
using System.Text.Json;
using Grpc.Net.Client;
using Kryolite.Grpc.DataService;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Wallet;
using MemoryPack;
using Microsoft.Extensions.Configuration;
using ServiceModel.Grpc.Client;
using ServiceModel.Grpc.Configuration;

namespace Kryolite.Cli;

public static class ContractCmd
{
    public static Command Build(Option<string?> nodeOption, IConfiguration configuration)
    {
        var contractCmd = new Command("contract", "Manage Contracts");
        var uploadCmd = new Command("upload", "Upload contract");

        var fromOption = new Argument<string>(name: "ADDRESS", description: "Address to send contract from (Contract Owner)")
        {
            
        };

        var fileOption = new Argument<string>(name: "PATH", description: "Path to WASM file");
        uploadCmd.AddArgument(fileOption);
        uploadCmd.AddArgument(fromOption);

        contractCmd.AddCommand(uploadCmd);

        uploadCmd.SetHandler(async (from, file, node) =>
        {
            var walletRepository = new WalletRepository(configuration);
            var account = walletRepository.GetAccount(from);

            if(account is null)
            {
                Console.WriteLine("Wallet not found from wallet.dat");
                Console.WriteLine("'" + from + "'");
                Environment.Exit(-1);
            }

            if(!File.Exists(file)) 
            {
                Console.WriteLine($"File does not exist {file}");
                Environment.Exit(-1);
            }

            var client = await Program.CreateClient(node);
            var manifestFile = Path.Combine(Path.GetDirectoryName(file)!, $"{Path.GetFileNameWithoutExtension(file)}.json");

            if (!File.Exists(manifestFile))
            {
                Console.WriteLine($"Manifest does not exist {manifestFile}");
            }

            var bytes = await File.ReadAllBytesAsync(file);
            var manifestJson = await File.ReadAllTextAsync(manifestFile, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize(manifestJson, SharedSourceGenerationContext.Default.ContractManifest);

            if (manifest == null)
            {
                Console.WriteLine($"Failed to deserialize {manifestFile}");
                Environment.Exit(-1);
            }

            var contract = new Contract(
                account.Address,
                manifest,
                bytes
            );

            var newContract = new NewContract(manifest, bytes);

            var payload = new TransactionPayload
            {
                Payload = newContract
            };

            var tx = new Transaction
            {
                TransactionType = TransactionType.CONTRACT,
                PublicKey = account.PublicKey,
                To = contract.ToAddress(bytes),
                Value = 0,
                Data = MemoryPackSerializer.Serialize(payload),
                Timestamp = 69
            };

            var privKey = walletRepository.GetPrivateKey(account.PublicKey);

            if (privKey is null)
            {
                Console.WriteLine($"Unable to load private key from {account.Address}");
                Environment.Exit(-1);
            }

            tx.Sign(privKey);

            var result = client.AddTransaction(new TransactionDto(tx));

            Console.WriteLine(result);
            Console.WriteLine(contract.ToAddress(bytes));
        }, fromOption, fileOption, nodeOption);

        return contractCmd;
    }
}
