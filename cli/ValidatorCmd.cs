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
        var disableCmd = new Command("disable", "Disable validator");

        validatorCmd.AddCommand(statusCmd);
        validatorCmd.AddCommand(enableCmd);
        validatorCmd.AddCommand(disableCmd);

        var stakeArg = new Argument<decimal>(name: "STAKE", description: "Amount to stake")
        {
            
        };

        var rewardAddressArg = new Argument<string>(name: "ADDRESS", description: "Address to receive stake rewards")
        {
            
        };

        enableCmd.AddArgument(stakeArg);
        enableCmd.AddArgument(rewardAddressArg);

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
                var keyAnswer = new
                {
                    NodeAddress = keys.PublicKey.ToAddress().ToString(),
                    RewardAddress = "",
                    Stake = 0,
                    Status = "Stake not set"
                };

                Console.WriteLine(JsonSerializer.Serialize(keyAnswer, Program.serializerOpts));

                return;
            }

            var stake = JsonSerializer.Deserialize<Validator>(text, Program.serializerOpts);

            var answer = new
            {
                NodeAddress = keys.PublicKey.ToAddress().ToString(),
                RewardAddress = stake?.RewardAddress.ToString(),
                Stake = stake?.Stake,
                Status = stake?.Stake >= Constant.MIN_STAKE ? "Enabled" : "Disabled"
            };

            Console.WriteLine(JsonSerializer.Serialize(answer, Program.serializerOpts));
        }, nodeOption);

        enableCmd.SetHandler(async (node, stake, rewardAddress) =>
        {
            await SendValidatorReg(node, stake, rewardAddress, configuration);
        }, nodeOption, stakeArg, rewardAddressArg);

        disableCmd.SetHandler(async (node) =>
        {
            await SendValidatorReg(node, 0, Address.NULL_ADDRESS, configuration);
        }, nodeOption);

        return validatorCmd;
    }

    private static async Task SendValidatorReg(string? node, decimal stake, Address rewardAddress, IConfiguration configuration)
    {
        node = node ?? await ZeroConf.DiscoverNodeAsync();

        var repository = new KeyRepository(configuration);
        var keys = repository.GetKey();

        using var http = new HttpClient();

        var tx = new Transaction
        {
            TransactionType = TransactionType.REG_VALIDATOR,
            PublicKey = keys.PublicKey,
            To = rewardAddress,
            Value = (long)(stake * Constant.DECIMAL_MULTIPLIER),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        tx.Sign(keys.PrivateKey);

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
    }
}
