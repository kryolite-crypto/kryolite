using System.CommandLine;
using System.Text;
using System.Text.Json;
using Grpc.Net.Client;
using Kryolite.Grpc.DataService;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Wallet;
using MemoryPack;
using Microsoft.Extensions.Configuration;
using ServiceModel.Grpc.Client;
using ServiceModel.Grpc.Configuration;

namespace Kryolite.Cli;

public static class SendCmd
{
    public static Command Build(Option<string?> nodeOption, IConfiguration configuration)
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
        
        var waitOption = new Option<bool>("--wait", "Wait for transaction to execute")
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
        sendCmd.AddOption(waitOption);

        sendCmd.SetHandler(async (from, to, amount, node, contractMethod, contractParams, wait) =>
        {
            var walletRepository = new WalletRepository(configuration);
            var account = walletRepository.GetAccount(from);

            if(account is null)
            {
                Console.WriteLine("Wallet not found from wallet.blob");
                Console.WriteLine("'" + from + "'");
                Environment.Exit(-1);
            }

            var client = await Program.CreateClient(node);

            TransactionPayload? transactionPayload = null;

            if (!string.IsNullOrEmpty(contractMethod))
            {
                transactionPayload = new TransactionPayload
                {
                    Payload = new CallMethod
                    {
                        Method = contractMethod,
                        Params = string.IsNullOrEmpty(contractParams) ? null : JsonSerializer.Deserialize<string[]>(contractParams, SharedSourceGenerationContext.Default.StringArray)
                    }
                };
            }

            var tx = new Transaction
            {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = account.PublicKey,
                To = to,
                Value = (ulong)(amount * Constant.DECIMAL_MULTIPLIER),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = transactionPayload != null ? MemoryPackSerializer.Serialize(transactionPayload) : null
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
        }, fromOption, toOption, amountOption, nodeOption, contractMethodOption, contractParamsOption, waitOption);

        return sendCmd;
    }
}
