using System.CommandLine;
using System.Configuration;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Kryolite.Cli;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;

public class Program
{
    public static JsonSerializerOptions serializerOpts = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private static async Task<int> Main(string[] args)
    {
        serializerOpts.PropertyNameCaseInsensitive = true;
        serializerOpts.Converters.Add(new AddressConverter());
        serializerOpts.Converters.Add(new PrivateKeyConverter());
        serializerOpts.Converters.Add(new PublicKeyConverter());
        serializerOpts.Converters.Add(new SHA256HashConverter());
        serializerOpts.Converters.Add(new SignatureConverter());
        serializerOpts.Converters.Add(new DifficultyConverter());

        PacketFormatter.Register<CallMethod>(Packet.CallMethod);
        PacketFormatter.Register<NewContract>(Packet.NewContract);

        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");

        // TODO: make this an option
        BlockchainService.DATA_PATH = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        Directory.CreateDirectory(BlockchainService.DATA_PATH);

        var rootCmd = new RootCommand("Kryolite CLI");

        var nodeOption = new Option<string?>(name: "--node-url", description: "Disable autodiscovery and connect to selected url");
        var pathOption = new Option<string?>(name: "--data-dir", description: "Kryolite data directory path", getDefaultValue: () => defaultDataDir);
        
        rootCmd.AddGlobalOption(nodeOption);
        rootCmd.AddGlobalOption(pathOption);

        var config = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        rootCmd.Add(ContractCmd.Build(nodeOption, config));
        rootCmd.Add(SendCmd.Build(nodeOption, config));
        rootCmd.Add(ValidatorCmd.Build(nodeOption, config));
        rootCmd.Add(WalletCmd.Build(config));

        return await rootCmd.InvokeAsync(args);
    }
}
