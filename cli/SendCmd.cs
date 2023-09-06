using System.CommandLine;
using System.Text;
using System.Text.Json;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;

namespace Kryolite.Cli;

public static class SendCmd
{
    public static Command Build(Option<string?> nodeOption)
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
                        Params = string.IsNullOrEmpty(contractParams) ? null : JsonSerializer.Deserialize<object[]>(contractParams, Program.serializerOpts)
                    }
                };
            }

            using var http = new HttpClient();

            var result = await http.GetAsync($"{node}/chain/tip");

            if (!result.IsSuccessStatusCode)
            {
                Console.WriteLine($"Request failed: {result.StatusCode}");
                Console.WriteLine(result.Content);
                return;
            }

            var parents = await JsonSerializer.DeserializeAsync<List<SHA256Hash>>(await result.Content.ReadAsStreamAsync(), Program.serializerOpts);

            if (parents is null)
            {
                Console.WriteLine("Parent hash download failed");
                return;
            }

            var lz4Options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithOmitAssemblyVersion(true);

            var tx = new Transaction
            {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = wallet.PublicKey,
                To = to,
                Value = (long)(amount * Constant.DECIMAL_MULTIPLIER),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = transactionPayload != null ? MessagePackSerializer.Serialize(transactionPayload, lz4Options) : null,
                Parents = parents
            };

            tx.Sign(wallet.PrivateKey);

            var json = JsonSerializer.Serialize(tx, Program.serializerOpts);
            var stringContent = new StringContent(json, UnicodeEncoding.UTF8, "application/json");

            var response = await http.PostAsync($"{node}/tx", stringContent);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Request failed: {response.StatusCode}");
                Console.WriteLine(response.Content);
                return;
            }

            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }, fromOption, toOption, amountOption, nodeOption, contractMethodOption, contractParamsOption);

        return sendCmd;
    }
}
