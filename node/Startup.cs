using System.Net;
using System.Net.WebSockets;
using System.Xml.Linq;
using DnsClient;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Node.Storage;
using Kryolite.Shared;
using LettuceEncrypt.Acme;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        app.MapServerSentEvents("/events");

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
                await AuthorizeAndAcceptConnection(app, context);
            });

            endpoints.MapControllers();
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var dataDir = Configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        Directory.CreateDirectory(dataDir);

        BlockchainService.DATA_PATH = dataDir;

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

        services.AddSingleton<IStorage, RocksDBStorage>()
                .AddSingleton<IStateCache, StateCache>()
                .AddSingleton<IKeyRepository, KeyRepository>()
                .AddScoped<IStoreRepository, StoreRepository>()
                .AddScoped<IStoreManager, StoreManager>()
                .AddScoped<IWalletRepository, WalletRepository>()
                .AddScoped<IWalletManager, WalletManager>()
                .AddScoped<IVerifier, Verifier>()
                .AddSingleton<INetworkManager, NetworkManager>()
                .AddSingleton<IMeshNetwork, MeshNetwork>()
                .AddSingleton<IExecutorFactory, ExecutorFactory>()
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
                .AddServerSentEvents()
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                    options.JsonSerializerOptions.Converters.Add(new AddressConverter());
                    options.JsonSerializerOptions.Converters.Add(new PrivateKeyConverter());
                    options.JsonSerializerOptions.Converters.Add(new PublicKeyConverter());
                    options.JsonSerializerOptions.Converters.Add(new SHA256HashConverter());
                    options.JsonSerializerOptions.Converters.Add(new SignatureConverter());
                    options.JsonSerializerOptions.Converters.Add(new DifficultyConverter());
                    options.JsonSerializerOptions.Converters.Add(new BigIntegerConverter());
                });
    }

    private async Task AuthorizeAndAcceptConnection(IApplicationBuilder app, HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();
        using var scope = app.ApplicationServices.CreateScope();
        var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();

        if (!int.TryParse(context.Request.Headers["kryo-apilevel"], out var apilevel))
        {
            logger.LogDebug("Received connection without api level, forcing disconnect...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "API_LEVEL_NOT_SET", CancellationToken.None);
            return;
        }

        if (apilevel < Constant.MIN_API_LEVEL)
        {
            logger.LogDebug("Incoming connection apilevel not supported ({apilevel}), forcing disconnect...", apilevel);
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "UNSUPPORTED_API_LEVEL", CancellationToken.None);
            return;
        }

        if (string.IsNullOrEmpty(context.Request.Headers["kryo-client-id"]))
        {
            logger.LogDebug("Received connection without client-id, forcing disconnect...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "CLIENT_ID_NOT_SET", CancellationToken.None);
            return;
        }

        if (!ulong.TryParse(context.Request.Headers["kryo-client-id"], out var clientId))
        {
            logger.LogDebug("Received connection with invalid client-id, forcing disconnect...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "INVALID_CLIENT_ID", CancellationToken.None);
            return;
        }

        if (networkManager.IsBanned(clientId))
        {
            logger.LogDebug("Received connection from banned node {clientId}, forcing disconnect...", clientId);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "BANNED_CLIENT", CancellationToken.None);
            return;
        }

        if (context.Request.Headers["kryo-network"] != Constant.NETWORK_NAME)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Wrong network: '{kryoNetwork}'", context.Request.Headers["kryo-network"].ToString());
            }

            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "WRONG_NETWORK", CancellationToken.None);
            return;
        }

        var mesh = app.ApplicationServices.GetRequiredService<IMeshNetwork>();

        if (clientId == mesh.GetServerId())
        {
            logger.LogDebug("Self connection, disconnecting client...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "SELF_CONNECTION", CancellationToken.None);
            return;
        }

        var url = context.Request.Headers["kryo-connect-to-url"];

        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, new UriCreationOptions(), out var uri))
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogInformation("Received connection from {url}", url.ToString());
            }

            var success = await Connection.TestConnectionAsync(uri);

            if (!success)
            {
                logger.LogInformation("Force disconnect {uri}, reason: url not reachable", uri);
                await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "URL_NOT_REACHABLE", CancellationToken.None);

                return;
            }

            var urlPeer = new Peer(webSocket, clientId, uri, ConnectionType.IN, true, apilevel);
            await mesh.AddSocketAsync(webSocket, urlPeer);

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

        address ??= context.Request.HttpContext.Connection.RemoteIpAddress;

        logger.LogInformation($"Received connection from {address}");

        List<Uri> hosts = new List<Uri>();

        var ports = context.Request.Headers["kryo-connect-to-ports"].ToString();

        foreach (var portStr in ports.Split(','))
        {
            if (int.TryParse(portStr, out var port))
            {
                var builder = new UriBuilder()
                {
                    Host = address!.ToString(),
                    Port = port
                };

                hosts.Add(builder.Uri);
            }
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

        var peer = new Peer(webSocket, clientId, bestUri, ConnectionType.IN, isReachable, apilevel);

        await mesh.AddSocketAsync(webSocket, peer);
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
