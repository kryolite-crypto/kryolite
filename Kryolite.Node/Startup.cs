using System.Net;
using System.Reflection;
using DnsClient;
using Kryolite.EventBus;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.API;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Network;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Transport.Websocket;
using Kryolite.Upnp;
using Kryolite.Wallet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public static class Startup
{
    public static WebApplicationBuilder BuildKryoliteNode(this WebApplicationBuilder builder, string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        DataDirectory.EnsureExists(config, args, out var dataDir, out var configPath);

        var walletRepository = new WalletRepository(config);
        walletRepository.Backup();

        var keyRepository = new KeyRepository(config);
        var pubKey = keyRepository.GetPublicKey();
        Console.WriteLine($"Server");
        Console.WriteLine($"\tPublic Key: {pubKey}");
        Console.WriteLine($"\tAddress: {pubKey.ToAddress()}");

        config = builder.Configuration
            .AddIniFile(configPath, optional: true)
            .AddCommandLine(args)
            .Build();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(x =>
        {
            x.FormatterName = "clean";
        });

        var loglevel = config.GetValue<string>("loglevel");

        if (loglevel == "default")
        {
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        }
        else
        {
            var level = loglevel switch
            {
                "critical" => LogLevel.Critical,
                "error" => LogLevel.Error,
                "warning" => LogLevel.Warning,
                "info" => LogLevel.Information,
                "debug" => LogLevel.Debug,
                "trace" => LogLevel.Trace,
                _ => throw new NotImplementedException($"loglevel '{loglevel}' not implemented")
            };

            builder.Logging.SetMinimumLevel(level);
        }

        builder.WebHost.ConfigureKestrel(x =>
        {
            var logger = x.ApplicationServices.GetRequiredService<ILogger<StartupLog>>();
            var config = x.ApplicationServices.GetRequiredService<IConfiguration>();

            var ssl = config.GetValue<bool>("ssl");
            var bind = config.GetValue<string>("bind");
            var port = config.GetValue<int>("port");
            var pfxpath = config.GetValue<string>("pfxpath");
            var pfxpass = config.GetValue<string>("pfxpass");

            x.Listen(IPAddress.Parse(bind ?? "127.0.0.1"), port, c =>
            {
                c.Protocols = HttpProtocols.Http1;

                if (ssl)
                {
                    c.UseHttps(pfxpath!, pfxpass);
                }

                logger.LogInformation("rpc:\t{schema}://{bind}:{rpcport}", ssl ? "https" : "http", bind, port);
            });
        });

        builder.Services.AddNodeServices();

        return builder;
    }

    public static void UseNodeMiddleware(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders();
        app.UseRouting();
        app.UseCors();
        app.UseWebSockets();
        app.UseKryoliteRpc();

        app.UseEndpoints(endpoints =>
        {
            endpoints
                .RegisterBaseApi()
                .RegisterEventApi()
                .RegisterWebsocketEndpoint();
        });
    }

    public static void AddNodeServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<IStorage, RocksDBStorage>()
                .AddSingleton<IStateCache, StateCache>()
                .AddSingleton<IKeyRepository, KeyRepository>()
                .AddSingleton<NodeTable>()
                .AddSingleton<IConnectionManager, ConnectionManager>()
                .AddSingleton<IClientFactory, ClientFactory>()
                .AddScoped<IStoreRepository, StoreRepository>()
                .AddScoped<IStoreManager, StoreManager>()
                .AddScoped<IWalletRepository, WalletRepository>()
                .AddScoped<IWalletManager, WalletManager>()
                .AddScoped<IVerifier, Verifier>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<ValidatorService>()
                .AddHostedService<MDnsService>()
                .AddHostedService<SyncManager>()
                .AddHostedService<PacketManager>()
                .AddHostedService<BroadcastManager>()
                .AddHostedService(p => (ConnectionManager)p.GetRequiredService<IConnectionManager>())
                .AddHostedService<DiscoveryManager>()
                .AddSingleton<ILookupClient>(new LookupClient())
                .AddSingleton<IEventBus, EventBus.EventBus>()
                .AddKryoliteRpcService((channel, sp) => new CalleeNodeService(channel, sp))
                .AddUpnpService()
                .AddRouting()
                .AddCors(opts => opts.AddDefaultPolicy(policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                ))
                .ConfigureHttpJsonOptions(options =>
                {
                    options.SerializerOptions.TypeInfoResolverChain.Add(SharedSourceGenerationContext.Default);
                    options.SerializerOptions.TypeInfoResolverChain.Add(NodeSourceGenerationContext.Default);
                    options.SerializerOptions.PropertyNameCaseInsensitive = true;
                    options.SerializerOptions.IncludeFields = true;
                    options.SerializerOptions.Converters.Add(new AddressConverter());
                    options.SerializerOptions.Converters.Add(new PrivateKeyConverter());
                    options.SerializerOptions.Converters.Add(new PublicKeyConverter());
                    options.SerializerOptions.Converters.Add(new SHA256HashConverter());
                    options.SerializerOptions.Converters.Add(new SignatureConverter());
                    options.SerializerOptions.Converters.Add(new DifficultyConverter());
                    options.SerializerOptions.Converters.Add(new BigIntegerConverter());
                });
    }
}

public static class StartupLogoAndVersion
{
    public static void Print()
    {
        var attr = Attribute.GetCustomAttribute(Assembly.GetEntryAssembly()!, typeof(AssemblyInformationalVersionAttribute))
            as AssemblyInformationalVersionAttribute;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($@"
 __                         .__  .__  __
|  | _________ ___.__. ____ |  | |__|/  |_  ____
|  |/ /\_  __ <   |  |/  _ \|  | |  \   __\/ __ \
|    <  |  | \/\___  (  <_> )  |_|  ||  | \  ___/
|__|_ \ |__|   / ____|\____/|____/__||__|  \___  >
     \/        \/                              \/
                            {attr?.InformationalVersion}
                                         ");
        Console.ForegroundColor = ConsoleColor.Gray;
    }
}

public static class DataDirectory
{
    public static void EnsureExists(IConfigurationRoot config, string[] args, out string dataDir, out string configPath)
    {
        dataDir = config.GetDataDir();
        Directory.CreateDirectory(dataDir);

        var versionPath = Path.Join(dataDir, $"store.version.{Constant.STORE_VERSION}");

        if (args.Contains("--resync") || !Path.Exists(versionPath))
        {
            var storeDir = Path.Join(dataDir, "store");

            if (Path.Exists(storeDir))
            {
                Directory.Delete(storeDir, true);
            }

            if (File.Exists(versionPath))
            {
                File.Delete(versionPath);
            }
        }

        var configVersion = Path.Join(dataDir, $"config.version.{Constant.CONFIG_VERSION}");

        if (args.Contains("--force-recreate") || !Path.Exists(configVersion))
        {
            var renamedTarget = $"{dataDir}-{DateTimeOffset.Now:yyyyMMddhhmmss}";
            if (Path.Exists(dataDir))
            {
                Directory.Move(dataDir, renamedTarget);
                Console.WriteLine($"Rename {dataDir} to {renamedTarget}");
            }
        }

        Directory.CreateDirectory(dataDir);

        if (!Path.Exists(configVersion))
        {
            File.WriteAllText(configVersion, Constant.CONFIG_VERSION);
        }

        DefaultConfig.EnsureExists(dataDir, out configPath);
    }
}

public static class DefaultConfig
{
    public static void EnsureExists(string dataDir, out string configPath)
    {
        configPath = Path.Join(dataDir, $"kryolite.conf");

        if (Path.Exists(configPath))
        {
            return;
        }

        var contents = """
; node communication settings
; 0.0.0.0 binds to all available endpoints
; 127.0.0.1 binds to local endpoint only
ssl="false"
bind="0.0.0.0"
port=11611
pfxpath=
pfxpass=

; public address to advertise to other nodes
; must be configured if certificate is in use with DNS name,
; or if node is behind reverse proxy
; accepted formats are "http://node.example.com" or "http://80.10.10.80:5000"
publicaddr=

; enable upnp mapping for listening port
upnp="true"

; enable mdns
mdns="true"

; use DNS discovery from "testnet.kryolite.io" to locate seed nodes
discovery="true"

; custom node to use for initial node download, in url form (http://localhost:11611 or http://80.80.80.80:11611)
seednode=

; connection timeout in seconds
timeout=30

; logging level (default, trace, debug, info, warning, error, critical)
loglevel="default"

""";

        File.WriteAllText(configPath, contents);
    }
}

public partial class ContractEvent
{
    public string Type { get; set; } = string.Empty;
    public object Event { get; set; } = string.Empty;
}

public class StartupLog
{

}
