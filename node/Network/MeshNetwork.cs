using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Kryolite.Shared;
using MessagePack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WatsonWebsocket;

namespace Kryolite.Node;

public class MeshNetwork : IMeshNetwork
{
    public event EventHandler<ConnectionEventArgs>? ClientConnected;
    public event EventHandler<DisconnectionEventArgs>? ClientDisconnected;
    public event EventHandler? ClientDropped;
    public event EventHandler<MessageEventArgs>? MessageReceived;

    public string? PublicAddress { get; set; }
    
    // TODO: better implementation
    public static event EventHandler<int>? ConnectedChanged;
    public static Guid ServerId { get; } = Guid.NewGuid();

    private int LocalPort { get; init; }
    private List<Uri> Endpoints { get; set; } = new();

    private WatsonWsServer wsServer;
    private ConcurrentDictionary<Guid, Peer> Peers = new();
    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly IServer server;
    private readonly IConfiguration configuration;
    private readonly ILogger<MeshNetwork> logger;
    private readonly StartupSequence startup;
    private MessagePackSerializerOptions lz4Options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithOmitAssemblyVersion(true);

    private TcpListener listener;

    public MeshNetwork(IServer server, IConfiguration configuration, ILogger<MeshNetwork> logger, StartupSequence startup)
    {
        logger.LogInformation("Initializing WebSocket server");

        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));

        // Reserve port for WsServer
        listener = new TcpListener(IPAddress.Any, 0)
        {
            ExclusiveAddressUse = false
        };

        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        wsServer = new WatsonWsServer("127.0.0.1", port, false);
        LocalPort = port;

        logger.LogDebug($"Binding WebSocket to localhost port {LocalPort}");

        wsServer.ClientConnected += (object? sender, ConnectionEventArgs args) => {
            _ = Task.Run(() => {
                try
                {
                    var network = configuration.GetValue<string?>("NetworkName") ?? "MAINNET";

                    if(args.HttpRequest.Headers["kryo-network"] != network) 
                    {
                        logger.LogDebug($"Wrong network: '{args.HttpRequest.Headers["kryo-network"]}'");
                        wsServer.DisconnectClient(args.Client.Guid);
                        return;
                    }

                    if(string.IsNullOrEmpty(args.HttpRequest.Headers["kryo-client-id"])) 
                    {
                        logger.LogDebug("Received connection without client-id, forcing disconnect...");
                        wsServer.DisconnectClient(args.Client.Guid);
                        return;
                    }

                    if (!Guid.TryParse(args.HttpRequest.Headers["kryo-client-id"], out var guid)) 
                    {
                        logger.LogDebug("Received connection with invalid client-id, forcing disconnect...");
                        wsServer.DisconnectClient(args.Client.Guid);
                        return;
                    }

                    if (guid == MeshNetwork.ServerId)
                    {
                        logger.LogDebug("Self connection, disconnecting client...");
                        wsServer.DisconnectClient(args.Client.Guid);
                        return;
                    }

                    var forwardedFor = args.HttpRequest.Headers["X-Forwarded-For"] ?? string.Empty;

                    if (string.IsNullOrEmpty(forwardedFor))
                    {
                        logger.LogDebug("X-Forwarded-For missing, disconnecting client...");
                        // something went wrong, this should always be set with builtin reverse proxy
                        wsServer.DisconnectClient(args.Client.Guid);
                        return;
                    }

                    logger.LogDebug("X-Forwarded-For = " + forwardedFor);

                    var address = forwardedFor
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

                    if (address == null)
                    {
                        // something went wrong, this should always be set with builtin reverse proxy
                        logger.LogDebug($"Failed to parse address from X-Forwarded-For header (value = {forwardedFor})");
                        return;
                    }

                    logger.LogInformation($"Received connection from {address}");

                    List<Uri> hosts = new List<Uri>();

                    var ports = args.HttpRequest.Headers["kryo-connect-to-ports"] ?? string.Empty;

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

                    if (Uri.TryCreate(args.HttpRequest.Headers["kryo-connect-to-url"], new UriCreationOptions(), out var uri))
                    {
                        hosts.Prepend(uri);
                    }
                    else
                    {
                        logger.LogInformation($"Received invalid kryo-connect-to-url: '{args.HttpRequest.Headers["kryo-connect-to-url"]}'");
                    }

                    Uri? reachable = null;
                    bool isReachable = false;

                    foreach (var host in hosts)
                    {
                        try
                        {
                            using var tcp = new TcpClient();
                            var ok = tcp.TestConnection(host.Host, host.Port);

                            if (!ok) 
                            {
                                logger.LogDebug($"Failed to open connection to {host}, skipping host...");
                                continue;
                            }

                            reachable = host;
                            isReachable = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, $"Connection failure: {host}");
                        }
                    }

                    if (reachable == null)
                    {
                        reachable = hosts.LastOrDefault(new UriBuilder
                        {
                            Host = address.ToString(),
                            Port = 80
                        }.Uri);
                    }

                    var peer = new RemoteClient(wsServer, reachable, ServerId, args.Client.Guid);

                    peer.LastSeen = DateTime.UtcNow;
                    peer.ConnectedSince = DateTime.UtcNow;
                    peer.ClientId = guid;
                    peer.IsReachable = isReachable;

                    Peers.TryAdd(args.Client.Guid, peer);

                    ClientConnected?.Invoke(peer, args);
                    ConnectedChanged?.Invoke(this, Peers.Count);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Invalid connection params from {}", args.Client.IpPort);
                    wsServer.DisconnectClient(args.Client.Guid);
                }
            });
        };

        wsServer.ClientDisconnected += (object? sender, DisconnectionEventArgs args) => {
            if(Peers.TryRemove(args.Client.Guid, out var client))
            {
                logger.LogInformation($"Client {client.Url.Host} disconnected");
            }

            ClientDisconnected?.Invoke(sender, args);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        wsServer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
            try
            {
                var eventArgs = new MessageEventArgs(args.Data, lz4Options);

                using(var _ = rwlock.EnterWriteLockEx())
                {
                    if(cache.TryGetValue(eventArgs.Message.Id, out var _)) {
                        return;
                    }

                    cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
                }
                
                if (Peers.TryGetValue(args.Client.Guid, out var peer))
                {
                    peer.LastSeen = DateTime.UtcNow;
                    MessageReceived?.Invoke(peer, eventArgs);
                }
                
                if (eventArgs.Rebroadcast) {
                    await BroadcastAsync(eventArgs.Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Received invalid message from {}", args.Client.IpPort);
                wsServer.DisconnectClient(args.Client.Guid);
            }
        };
    }

    public void Start()
    {
        startup.Application.WaitOne();

        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

        Endpoints = addresses
            .Where(x => x is not null)
            .Select(x => new Uri(x))
            .ToList();

        logger.LogInformation("Starting Websocket server");
        try
        {
            listener.Stop();
            wsServer.Start();
            logger.LogInformation("Websocket server started");
        } 
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start WebSocket server");
        }
    }

    public async Task BroadcastAsync(Message msg)
    {
        if (msg.NodeId == null) {
            msg.NodeId = ServerId;
        }

        var bytes = MessagePackSerializer.Serialize(msg, lz4Options);

        using(var _ = rwlock.EnterWriteLockEx())
        {
            cache.Set(msg.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
        }

        await Parallel.ForEachAsync(Peers, async (peer, token) => 
        {
            await peer.Value.SendAsync(msg);
        });
    }

    public async Task<bool> AddNode(Uri url, Guid clientId)
    {
        using(var _ = rwlock.EnterWriteLockEx())
        {
            if (clientId == ServerId)
            {
                return false;
            }

            if (Peers.Any(x => x.Value.Url == url))
            {
                return false;
            }

            // TODO: Fails if Peer has not received any messages (and clientid)
            /*if (Peers.Values.Any(x => x.ClientId == clientId))
            {
                return false;
            }*/

            // filter out clients connected to local wsServer
            var connectedTo = Peers.Where(x => x.Value is Peer).ToDictionary(x => x.Key, x => x.Value);

            if (connectedTo.Count >= Constant.MAX_PEERS)
            {
                var client = connectedTo.OrderBy(x => x.Value.ConnectedSince).First();
                client.Value.Disconnect();

                if (Peers.TryRemove(client.Key, out var _))
                {
                    logger.LogInformation($"Disconnected from {client.Key}");
                }
            }
        }

        logger.LogInformation($"Connecting to {url.ToHostname()}");

        var networkName = configuration.GetValue<string?>("NetworkName") ?? "MAINNET";
        var publicUrl = configuration.GetValue<string>("PublicUrl");

        var peer = new LocalClient(url, publicUrl, networkName, Endpoints);

        peer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => 
        {
            try
            {
                var eventArgs = new MessageEventArgs(args.Data, lz4Options);

                using(var _ = rwlock.EnterWriteLockEx())
                {
                    if(cache.TryGetValue(eventArgs.Message.Id, out var _)) 
                    {
                        return;
                    }

                    cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
                }
                
                MessageReceived?.Invoke(sender, eventArgs);
                
                if (eventArgs.Rebroadcast) 
                {
                    await BroadcastAsync(eventArgs.Message);
                }
            } 
            catch (Exception ex)
            {
                logger.LogError(ex, "Received invalid message from {}", args.Client.IpPort);
                peer.Disconnect();
            }
        };

        peer.Dropped += (object? sender, EventArgs args) => 
        {
            if (sender is LocalClient peer)
            {
                Peers.TryRemove(peer.ConnectionId, out var _);
            }

            ClientDropped?.Invoke(sender, EventArgs.Empty);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        if (await peer.StartWithTimeoutAsync())
        {
            peer.IsReachable = true;
            logger.LogInformation($"Connected to {url.ToHostname()}");
            Peers.TryAdd(peer.ConnectionId, peer);
            ConnectedChanged?.Invoke(this, Peers.Count);
            return true;
        }

        return false;
    }

    public Dictionary<string, Peer> GetPeers()
    {
        return Peers.Values
            .DistinctBy(x => x.Url)
            .ToDictionary(x => x.Url.ToHostname(), x => x);
    }

    public int GetLocalPort()
    {
        return LocalPort;
    }

    public List<Uri> GetEndpoints()
    {
        return Endpoints;
    }
}
