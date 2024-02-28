using System.CommandLine;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Kryolite.Cli;
using Kryolite.Grpc.DataService;
using Kryolite.Node;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using ServiceModel.Grpc.Client;
using ServiceModel.Grpc.Configuration;

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

    public static async Task<IDataService> CreateClient(string? node)
    {
        node ??= await ZeroConf.DiscoverNodeAsync();

        var opts = new ServiceModelGrpcClientOptions
        {
            MarshallerFactory = MemoryPackMarshallerFactory.Default
        };

        var clientFactory = new ClientFactory(opts)
            .AddDataServiceClient();

        return clientFactory.CreateClient<IDataService>(GrpcChannel.ForAddress(node, new GrpcChannelOptions
        {
            HttpClient = new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                KeepAlivePingDelay = TimeSpan.FromSeconds(10),
                KeepAlivePingTimeout =  TimeSpan.FromSeconds(5)
            })
        }));
    }
}
