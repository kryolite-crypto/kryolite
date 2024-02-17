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
            node = node ?? await ZeroConf.DiscoverNodeAsync();

            var repository = new KeyRepository(configuration);
            var keys = repository.GetKey();

            using var http = new HttpClient();

            var result = await http.GetAsync($"{node}/validator/{keys.PublicKey.ToAddress().ToString()}");

            if (!result.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch validator status: {result.StatusCode}, {result.ReasonPhrase}");
                return;
            }

            var text = await result.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine($"NodeAddress: {keys.PublicKey.ToAddress().ToString()}");
                Console.WriteLine($"RewardAddress: ");
                Console.WriteLine($"Stake: 0");
                Console.WriteLine($"Status: Stake not set");

                return;
            }

            var stake = JsonSerializer.Deserialize(text, SharedSourceGenerationContext.Default.Validator);

            Console.WriteLine($"NodeAddress: {stake!.NodeAddress}");
            Console.WriteLine($"RewardAddress: {stake!.RewardAddress}");
            Console.WriteLine($"Stake: {stake!.Stake}");
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
        node ??= await ZeroConf.DiscoverNodeAsync();

        var repository = new KeyRepository(configuration);
        var keys = repository.GetKey();

        using var http = new HttpClient();

        var tx = new Transaction
        {
            TransactionType = txType,
            PublicKey = keys.PublicKey,
            To = rewardAddress ?? Address.NULL_ADDRESS,
            Value = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        tx.Sign(keys.PrivateKey);

        var json = JsonSerializer.Serialize(tx, SharedSourceGenerationContext.Default.Transaction);
        var stringContent = new StringContent(json, UnicodeEncoding.UTF8, "application/json");

        var response = await http.PostAsync($"{node}/tx", stringContent);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Request failed: {response.StatusCode}");
            Console.WriteLine(response.Content);
            return;
        }

        Console.WriteLine(await response.Content.ReadAsStringAsync());
    }
}
