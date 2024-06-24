using System.CommandLine;
using Kryolite.Cli;
using Kryolite.Mdns;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;

public class Program
{
    private static async Task<int> Main(string[] args)
    {
        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        Directory.CreateDirectory(defaultDataDir);

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

    public static async Task<HttpClient> CreateClient(string? node)
    {
        if (node is null)
        {
            using var mdns = new MdnsClient();

            var nodes = await mdns.Query(Constant.MDNS_SERVICE_NAME);
            node = nodes.FirstOrDefault();
        }

        var client = new HttpClient()
        {
            BaseAddress = new (node!)
        };

        return client;
    }
}
