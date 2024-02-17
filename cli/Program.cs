using System.CommandLine;
using System.Text.Json;
using Kryolite.Cli;
using Kryolite.Node;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;

public class Program
{
    private static async Task<int> Main(string[] args)
    {
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
