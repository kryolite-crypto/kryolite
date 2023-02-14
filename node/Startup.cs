using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Serialization;
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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = null
        });

        app.UseWebSockets();
        app.UseRouting();
        app.UseCors();
        app.UseEndpoints(endpoints =>
        {
            // for let's encrypt
            endpoints.MapGroup(".well-known").MapGet("{**catch-all}", async context =>
            {
                await context.Response.WriteAsync("OK");
            });

            endpoints.Map("whatisthis/{**catch-all}", async context => 
            {
                await context.Response.WriteAsync("OK");
            });

            endpoints.Map("hive/{**catch-all}", async context => 
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                    var network = Configuration.GetValue<string?>("NetworkName") ?? "MAINNET";
                    var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();

                    if(context.Request.Headers["kryo-network"] != network) 
                    {
                        logger.LogDebug($"Wrong network: '{context.Request.Headers["kryo-network"]}'");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    if(string.IsNullOrEmpty(context.Request.Headers["kryo-client-id"])) 
                    {
                        logger.LogDebug("Received connection without client-id, forcing disconnect...");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    if (!ulong.TryParse(context.Request.Headers["kryo-client-id"], out var clientId)) 
                    {
                        logger.LogDebug("Received connection with invalid client-id, forcing disconnect...");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    var mesh = app.ApplicationServices.GetRequiredService<IMeshNetwork>();

                    if (clientId == mesh.GetServerId())
                    {
                        logger.LogDebug("Self connection, disconnecting client...");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    IPAddress? address = null;
                    var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();

                    logger.LogDebug("X-Forwarded-For = " + forwardedFor);

                    if (!string.IsNullOrEmpty(forwardedFor))
                    {
                        address = forwardedFor
                            .Split(",")
                            .Select(x => IPAddress.Parse(x.Trim()))
                            .Reverse()
                            .Where(x => x.IsPublic())
                            .LastOrDefault();

                        if (address == null)
                        {
                            address = forwardedFor
                                .Split(",")
                                .Select(x => IPAddress.Parse(x.Trim()))
                                .Reverse()
                                .LastOrDefault();
                        }
                    }

                    if (address == null)
                    {
                        address = context.Request.HttpContext.Connection.RemoteIpAddress;
                    }

                    logger.LogInformation($"Received connection from {address}");

                    List<Uri> hosts = new List<Uri>();

                    var ports = context.Request.Headers["kryo-connect-to-ports"].ToString();

                    Console.WriteLine("Possible ports: " + ports);

                    foreach (var portStr in ports.Split(','))
                    {
                        if (int.TryParse(portStr, out var port))
                        {
                            var builder = new UriBuilder()
                            {
                                Host = address.ToString(),
                                Port = port
                            };

                            hosts.Add(builder.Uri);
                        }
                    }

                    var url = context.Request.Headers["kryo-connect-to-url"];

                    if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, new UriCreationOptions(), out var uri))
                    {
                        hosts.Insert(0, uri);
                    }

                    Uri? bestUri = null;
                    bool isReachable = false;

                    foreach (var host in hosts)
                    {
                        try
                        {
                            var success = await Connection.TestConnectionAsync(host);

                            if (!success) 
                            {
                                logger.LogDebug($"Failed to open connection to {host}, skipping host...");
                                continue;
                            }

                            bestUri = host;
                            isReachable = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, $"Connection failure: {host}");
                        }
                    }

                    if (bestUri == null)
                    {
                        bestUri = hosts.LastOrDefault();
                    }

                    if (bestUri == null)
                    {
                        bestUri = new UriBuilder
                        {
                            Host = context.Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                            Port = context.Request.HttpContext.Connection.RemotePort
                        }.Uri;
                    }

                    var peer = new Peer(webSocket, clientId, bestUri, ConnectionType.IN, isReachable);

                    await mesh.AddSocketAsync(webSocket, peer);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });

            endpoints.MapControllers();
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
        PacketFormatter.Register<CallMethod>(Packet.CallMethod);
        PacketFormatter.Register<NewContract>(Packet.NewContract);
        PacketFormatter.Register<NodeList>(Packet.NodeList);
        PacketFormatter.Register<QueryNodeList>(Packet.QueryNodeList);

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
                .AddCors(opts => opts.AddDefaultPolicy(policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                ))
                .AddControllers().AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                    options.JsonSerializerOptions.Converters.Add(new AddressConverter());
                    options.JsonSerializerOptions.Converters.Add(new NonceConverter());
                    options.JsonSerializerOptions.Converters.Add(new PrivateKeyConverter());
                    options.JsonSerializerOptions.Converters.Add(new PublicKeyConverter());
                    options.JsonSerializerOptions.Converters.Add(new SHA256HashConverter());
                    options.JsonSerializerOptions.Converters.Add(new SignatureConverter());
                    options.JsonSerializerOptions.Converters.Add(new DifficultyConverter());
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
