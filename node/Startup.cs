using System.Net;
using System.Xml.Linq;
using DnsClient;
using Kryolite.Shared;
using LettuceEncrypt.Acme;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
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
        var dataDir = Configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        Directory.CreateDirectory(dataDir);

        BlockchainService.DATA_PATH = dataDir;

        PacketFormatter.Register<NodeInfo>(Packet.NodeInfo);
        PacketFormatter.Register<Blockchain>(Packet.Blockchain);
        PacketFormatter.Register<NewBlock>(Packet.NewBlock);
        PacketFormatter.Register<QueryNodeInfo>(Packet.QueryNodeInfo);
        PacketFormatter.Register<RequestChainSync>(Packet.RequestChainSync);
        PacketFormatter.Register<TransactionData>(Packet.TransactionData);
        PacketFormatter.Register<VoteBatch>(Packet.VoteBatch);
        PacketFormatter.Register<NodeDiscovery>(Packet.NodeDiscovery);
        PacketFormatter.Register<NodeList>(Packet.NodeList);
        PacketFormatter.Register<CallMethod>(Packet.CallMethod);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataDir))
            .AddKeyManagementOptions(options =>
            {
                options.XmlEncryptor = new DummyXmlEncryptor();
            })
            .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
            {
                EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
                ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
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
                .AddHostedService<POSService>()
                .AddHostedService<UPnPService>()
                .AddHostedService<MDNSService>()
                .AddSingleton<StartupSequence>()
                .AddSingleton<ILookupClient>(new LookupClient())
                .AddRouting()
                .AddControllers()
                .AddNewtonsoftJson(options => {
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                });
    }

    class DummyXmlEncryptor : IXmlEncryptor
    {
        public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        {
            return new EncryptedXmlInfo(plaintextElement, typeof(DummyXmlDecryptor));
        }
    }

    class DummyXmlDecryptor : IXmlDecryptor
    {
        public XElement Decrypt(XElement encryptedElement)
        {
            return encryptedElement;
        }
    }
}
