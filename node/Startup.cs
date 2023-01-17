using System.Net;
using Kryolite.Shared;
using LettuceEncrypt.Acme;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Yarp.ReverseProxy.Configuration;

namespace Kryolite.Node;

public class Startup
{
    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseForwardedHeaders();

        if (Configuration.GetSection("Kestrel").GetSection("Endpoints").AsEnumerable().Any(x => x.Value is not null && x.Value.StartsWith(Uri.UriSchemeHttps)))
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGroup(".well-known").MapGet("{**catch-all}", async context =>
            {
                await context.Response.WriteAsync("OK");
            });
            endpoints.MapControllers();
            endpoints.MapReverseProxy();
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var dataDir = Configuration.GetValue<string>("data-dir") ?? "data";
        Directory.CreateDirectory(dataDir);

        BlockchainService.DATA_PATH = dataDir;

        PacketFormatter.Register<NodeInfo>(Packet.NodeInfo);
        PacketFormatter.Register<Blockchain>(Packet.Blockchain);
        PacketFormatter.Register<PosBlock>(Packet.PosBlock);
        PacketFormatter.Register<QueryNodeInfo>(Packet.QueryNodeInfo);
        PacketFormatter.Register<RequestChainSync>(Packet.RequestChainSync);
        PacketFormatter.Register<TransactionData>(Packet.TransactionData);
        PacketFormatter.Register<VoteBatch>(Packet.VoteBatch);
        PacketFormatter.Register<NodeDiscovery>(Packet.NodeDiscovery);
        PacketFormatter.Register<NodeList>(Packet.NodeList);
        PacketFormatter.Register<CallMethod>(Packet.CallMethod);

        services.Configure<ForwardedHeadersOptions>(options =>
          {
              options.ForwardedHeaders =
                  ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        });

        if(Configuration.GetSection("LettuceEncrypt").Exists())
        {
            services.AddLettuceEncrypt(c => c.AllowedChallengeTypes = ChallengeType.Http01);
        }

        services.AddSingleton<IProxyConfigProvider, ProxyConfigProvider>()
                .AddReverseProxy();

        services.AddSingleton<IBlockchainManager, BlockchainManager>()
                .AddSingleton<Lazy<IBlockchainManager>>(c => new Lazy<IBlockchainManager>(c.GetService<IBlockchainManager>()!))
                .AddSingleton<INetworkManager, NetworkManager>()
                .AddSingleton<IMempoolManager, MempoolManager>()
                .AddSingleton<IWalletManager, WalletManager>()
                .AddSingleton<IMeshNetwork, MeshNetwork>()
                .AddHostedService<NetworkService>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<MempoolService>()
                .AddHostedService<SampoService>()
                .AddSingleton<StartupSequence>()
                .AddRouting()
                .AddControllers()
                .AddNewtonsoftJson(options => {
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                });
    }
}