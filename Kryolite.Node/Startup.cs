using System.Net;
using System.Reflection;
using DnsClient;
using Kryolite.EventBus;
using Kryolite.Node.API;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Network;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Upnp;
using MemoryPack;
using MemoryPack.Formatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceModel.Grpc.Client;

namespace Kryolite.Node;

public static class Startup
{
    public static WebApplicationBuilder BuildKryoliteNode(this WebApplicationBuilder builder, string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        DataDirectory.EnsureExists(config, args, out var dataDir, out var configPath);

        RegisterFormatters();

        var walletRepository = new WalletRepository(config);
        walletRepository.Backup();

        var keyRepository = new KeyRepository(config);
        Console.WriteLine($"Server");
        Console.WriteLine($"\tPublic Key: {keyRepository.GetKey().PublicKey}");
        Console.WriteLine($"\tAddress: {keyRepository.GetKey().Address}");

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

            // GRPC, Node Communication
            x.Listen(IPAddress.Parse(bind ?? "127.0.0.1"), port, c => 
            { 
                c.Protocols = HttpProtocols.Http2;

                if (ssl)
                {
                    c.UseHttps(pfxpath!, pfxpass);
                }

                logger.LogInformation("rpc:\t{schema}://{bind}:{rpcport}", ssl ? "https" : "http", bind, port);
            });

            var rpcssl = config.GetValue<string?>("rpcssl");
            var rpcbind = config.GetValue<string>("rpcbind");
            var rpcportstr = config.GetValue<string?>("rpcport");
            var rpcpfxpath = config.GetValue<string>("pfxpath");
            var rpcpfxpass = config.GetValue<string>("pfxpass");

            if (string.IsNullOrEmpty(rpcbind) || !int.TryParse(rpcportstr, out var rpcport))
            {
                return;
            }

            // API
            x.Listen(IPAddress.Parse(rpcbind), rpcport, c => 
            {
                c.Protocols = HttpProtocols.Http1;
                
                if (ssl)
                {
                    c.UseHttps(rpcpfxpath!, rpcpfxpass);
                }

                logger.LogInformation("rpc-json:\t{schema}://{bind}:{rpcport}", rpcssl == "true" ? "https" : "http", rpcbind, rpcport);
            });
        });

        builder.Services.AddNodeServices();

        return builder;
    }

    public static void RegisterFormatters()
    {
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<BlockBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NodeBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NodeInfoResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<PendingResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<TransactionBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewRangeResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<VoteBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<WalletContainer>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Address>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Block>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<CallMethod>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Contract>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ContractManifest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ContractMethod>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ContractParam>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Effect>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<PrivateKey>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<PublicKey>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Ledger>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NewContract>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<SHA256Hash>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Signature>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Validator>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Token>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Transaction>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<TransactionPayload>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<View>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Vote>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Wallet>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Difficulty>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<TransactionDto>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ChainState>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<AuthResponse>());

        var txPayloadFormatter = new DynamicUnionFormatter<ITransactionPayload>(new[]
        {
            ((ushort)0, typeof(NewContract)),
            ((ushort)1, typeof(CallMethod))
        });

        MemoryPackFormatterProvider.Register(txPayloadFormatter);

        var packetFormatter = new DynamicUnionFormatter<IBroadcast>(new[]
        {
            ((ushort)1, typeof(NodeBroadcast)),
            ((ushort)5, typeof(ViewBroadcast)),
            ((ushort)6, typeof(BlockBroadcast)),
            ((ushort)9, typeof(VoteBroadcast)),
            ((ushort)12, typeof(TransactionBroadcast))
        });

        MemoryPackFormatterProvider.Register(packetFormatter);
    }

    public static void UseNodeMiddleware(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        });

        app.UseRouting();
        app.UseCors();

        app.UseEndpoints(endpoints => endpoints
            .RegisterBaseApi()
            .RegisterEventApi()
            .MapNodeService()
        );
    }

    public static void AddNodeServices(this IServiceCollection services)
    {
        var clientFactory = new ClientFactory()
            .AddNodeServiceClient();

        services.AddHttpContextAccessor();

        services.AddSingleton<IStorage, RocksDBStorage>()
                .AddSingleton<IStateCache, StateCache>()
                .AddSingleton<IKeyRepository, KeyRepository>()
                .AddSingleton<NodeTable>()
                .AddSingleton<IConnectionManager, ConnectionManager>()
                .AddScoped<INodeService, NodeService>()
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
                .AddSingleton((sp) => clientFactory)
                .AddServiceModelGrpc().Services
                .AddNodeServiceOptions(opts => {})
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
        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        dataDir = config.GetValue("data-dir", defaultDataDir) ?? defaultDataDir;

        Directory.CreateDirectory(dataDir);

        var versionPath = Path.Join(dataDir, $"store.version.{Constant.STORE_VERSION}");

        if (args.Contains("--resync") || !Path.Exists(versionPath))
        {
            Console.WriteLine("Performing full resync");
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

        var configVersion = Path.Join(dataDir, $"config.version.{Constant.STORE_VERSION}");

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

; rpc-json configuration, not enabled by default
rpcssl=
rpcbind=
rpcport=
rpcpfxpath=
rpcpfxpass=

; use DNS discovery from "testnet.kryolite.io" to locate seed nodes
discovery="true"

; custom node to use for initial node download, in url form (http://localhost:11611)
seednode=

; connection timeout in seconds
timeout=30

; custom data directory location (default ~/.kryolite or %userprofile%\.kryolite)
datadir=

; logging level (default, trace, debuf, info, warning, error, critical)
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