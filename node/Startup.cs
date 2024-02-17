using DnsClient;
using Kryolite.EventBus;
using Kryolite.Node.API;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using LettuceEncrypt.Acme;
using MemoryPack;
using MemoryPack.Formatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node;

public class Startup
{
    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public static void RegisterFormatters()
    {
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NodeCandidate>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Message>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<Reply>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<BlockBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<BlockRequest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<BlockResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<HeightRequest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<HeightResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NodeBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NodeInfoRequest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<NodeInfoResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<PendingRequest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<PendingResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<TransactionBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<TransactionRequest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<TransactionResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewRequestByHash>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewRequestById>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewRequestByRange>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<ViewRangeResponse>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<VoteBroadcast>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<VoteRequest>());
        MemoryPackFormatterProvider.Register(new MemoryPackableFormatter<VoteResponse>());
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

        var txPayloadFormatter = new DynamicUnionFormatter<ITransactionPayload>(new[]
        {
            ((ushort)0, typeof(NewContract)),
            ((ushort)1, typeof(CallMethod))
        });

        MemoryPackFormatterProvider.Register(txPayloadFormatter);

        var packetFormatter = new DynamicUnionFormatter<IPacket>(new[]
        {
            ((ushort)0, typeof(NodeInfoRequest)),
            ((ushort)1, typeof(NodeInfoResponse)),
            ((ushort)4, typeof(HeightRequest)),
            ((ushort)5, typeof(HeightResponse)),
            ((ushort)6, typeof(BlockRequest)),
            ((ushort)7, typeof(BlockResponse)),
            ((ushort)8, typeof(VoteRequest)),
            ((ushort)9, typeof(VoteResponse)),
            ((ushort)10, typeof(ViewRequestByHash)),
            ((ushort)11, typeof(ViewRequestById)),
            ((ushort)12, typeof(ViewResponse)),
            ((ushort)13, typeof(TransactionRequest)),
            ((ushort)14, typeof(TransactionResponse)),
            ((ushort)15, typeof(PendingRequest)),
            ((ushort)16, typeof(PendingResponse)),
            ((ushort)17, typeof(ViewRequestByRange)),
            ((ushort)18, typeof(ViewRangeResponse)),
            ((ushort)100, typeof(NodeBroadcast)),
            ((ushort)101, typeof(ViewBroadcast)),
            ((ushort)102, typeof(BlockBroadcast)),
            ((ushort)103, typeof(VoteBroadcast)),
            ((ushort)104, typeof(TransactionBroadcast))
        });

        MemoryPackFormatterProvider.Register(txPayloadFormatter);
    }

    public void Configure(IApplicationBuilder app)
    {
        if (Configuration.GetSection("Kestrel").GetSection("Endpoints").AsEnumerable().Any(x => x.Value is not null && x.Value.StartsWith(Uri.UriSchemeHttps)))
        {
            app.UseHttpsRedirection();
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = null
        });

        app.UseWebSockets();
        app.UseRouting();
        app.UseCors();

        app.UseEndpoints(endpoints => endpoints
            .RegisterAuthApi()
            .RegisterBaseApi()
            .RegisterEventApi()
        );
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var dataDir = Configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        Directory.CreateDirectory(dataDir);

        BlockchainService.DATA_PATH = dataDir;

        if(Configuration.GetSection("LettuceEncrypt").Exists())
        {
            services.AddLettuceEncrypt(c => c.AllowedChallengeTypes = ChallengeType.Http01);
        }

        services.AddSingleton<IStorage, RocksDBStorage>()
                .AddSingleton<IStateCache, StateCache>()
                .AddSingleton<IKeyRepository, KeyRepository>()
                .AddSingleton<INetworkManager, NetworkManager>()
                .AddSingleton<IMeshNetwork, MeshNetwork>()
                .AddScoped<IStoreRepository, StoreRepository>()
                .AddScoped<IStoreManager, StoreManager>()
                .AddScoped<IWalletRepository, WalletRepository>()
                .AddScoped<IWalletManager, WalletManager>()
                .AddScoped<IVerifier, Verifier>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<UPnPService>()
                .AddHostedService<NetworkService>()
                .AddHostedService<ValidatorService>()
                .AddHostedService<MDNSService>()
                .AddSingleton<IBufferService<Chain, SyncService>, SyncService>()
                .AddHostedService(p => (SyncService)p.GetRequiredService<IBufferService<Chain, SyncService>>())
                .AddSingleton<ILookupClient>(new LookupClient())
                .AddSingleton<IEventBus, EventBus.EventBus>()
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

public partial class ContractEvent
{
    public string Type { get; set; } = string.Empty;
    public object Event { get; set; } = string.Empty;
}