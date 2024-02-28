using System.Collections.Immutable;
using System.CommandLine;
using System.Text;
using System.Text.Json;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Cli;

public static class ValidatorCmd
{
    public static Command Build(Option<string?> nodeOption, IConfiguration configuration)
    {
        var validatorCmd = new Command("validator", "Manage Validator service");
        var statusCmd = new Command("status", "Query validator status");
        var enableCmd = new Command("enable", "Enable validator");
        var updateCmd = new Command("update", "Update reward address");
        var disableCmd = new Command("disable", "Disable validator");

        validatorCmd.AddCommand(statusCmd);
        validatorCmd.AddCommand(enableCmd);
        validatorCmd.AddCommand(updateCmd);
        validatorCmd.AddCommand(disableCmd);

        var rewardAddressArg = new Argument<string>(name: "ADDRESS", description: "Address to receive stake rewards")
        {
            
        };

        enableCmd.AddArgument(rewardAddressArg);
        updateCmd.AddArgument(rewardAddressArg);

        statusCmd.SetHandler(async (node) =>
        {
            var client = await Program.CreateClient(node);

            var repository = new KeyRepository(configuration);
            var pubKey = repository.GetPublicKey();

            var validator = client.GetValidator(pubKey.ToAddress().ToString());

            if (validator is null)
            {
                Console.WriteLine($"NodeAddress: {pubKey}");
                Console.WriteLine($"RewardAddress: ");
                Console.WriteLine($"Stake: 0");
                Console.WriteLine($"Status: Stake not set");

                return;
            }

            Console.WriteLine($"NodeAddress: {validator.NodeAddress}");
            Console.WriteLine($"RewardAddress: {validator.RewardAddress}");
            Console.WriteLine($"Stake: {validator.Stake}");
            Console.WriteLine($"Status: Enabled");
        }, nodeOption);

        enableCmd.SetHandler(async (node, rewardAddress) =>
        {
            await SendValidatorReg(node, TransactionType.REGISTER_VALIDATOR, rewardAddress, configuration);
        }, nodeOption, rewardAddressArg);

        updateCmd.SetHandler(async (node, rewardAddress) =>
        {
            await SendValidatorReg(node, TransactionType.REGISTER_VALIDATOR, rewardAddress, configuration);
        }, nodeOption, rewardAddressArg);

        disableCmd.SetHandler(async (node) =>
        {
            await SendValidatorReg(node, TransactionType.DEREGISTER_VALIDATOR, Address.NULL_ADDRESS, configuration);
        }, nodeOption);

        return validatorCmd;
    }

    private static async Task SendValidatorReg(string? node, TransactionType txType, Address? rewardAddress, IConfiguration configuration)
    {
        var client = await Program.CreateClient(node);
        var repository = new KeyRepository(configuration);

        var tx = new Transaction
        {
            TransactionType = txType,
            PublicKey = repository.GetPublicKey(),
            To = rewardAddress ?? Address.NULL_ADDRESS,
            Value = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        tx.Sign(repository.GetPrivateKey());

        var result = client.AddTransaction(new TransactionDto(tx));

        Console.WriteLine(result);
    }
}
