using System.CommandLine;
using System.Text;
using System.Text.Json;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Configuration;

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

        var fileOption = new Argument<string>(name: "PATH", description: "Path to WASM file to upload.")
        {
            
        };

        uploadCmd.AddArgument(fileOption);
        uploadCmd.AddArgument(fromOption);

        contractCmd.AddCommand(uploadCmd);

        uploadCmd.SetHandler(async (from, file, node) =>
        {
            var walletRepository = new WalletRepository(configuration);
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
                Environment.Exit(-1);
            }

            var bytes = File.ReadAllBytes(file);

            var contract = new Contract(
                wallet.PublicKey.ToAddress(),
                bytes
            );

            var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
            var newContract = new NewContract(bytes);

            var payload = new TransactionPayload
            {
                Payload = newContract
            };

            var tx = new Transaction
            {
                TransactionType = TransactionType.CONTRACT,
                PublicKey = wallet.PublicKey,
                To = contract.ToAddress(bytes),
                Value = 0,
                Data = MessagePackSerializer.Serialize(payload, lz4Options),
                Timestamp = 69
            };

            tx.Sign(wallet.PrivateKey);
            
            var json = JsonSerializer.Serialize(tx, Program.serializerOpts);
            var stringContent = new StringContent(json, UnicodeEncoding.UTF8, "application/json");

            using var http = new HttpClient();
            var response = await http.PostAsync($"{node}/tx", stringContent);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Request failed: {response.StatusCode}");
                Console.WriteLine(response.Content);
                return;
            }

            Console.WriteLine(await response.Content.ReadAsStringAsync());
            Console.WriteLine(contract.ToAddress(bytes));
        }, fromOption, fileOption, nodeOption);

        return contractCmd;
    }
}
