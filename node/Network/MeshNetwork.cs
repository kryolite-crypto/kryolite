using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Kryolite.Shared;
using MessagePack;
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
    
    // TODO: better implementation
    public static event EventHandler<int>? ConnectedChanged;
    public static Guid ServerId { get; } = Guid.NewGuid();

    private int LocalPort { get; init; }
    private int RemotePort { get; init; }

    private WatsonWsServer wsServer;
    private ConcurrentDictionary<Guid, Peer> Peers = new();
    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly IConfiguration configuration;
    private readonly ILogger<MeshNetwork> logger;
    private MessagePackSerializerOptions lz4Options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithOmitAssemblyVersion(true);

    private TcpListener listener;

    public MeshNetwork(IConfiguration configuration, ILogger<MeshNetwork> logger)
    {
        logger.LogInformation("Initializing WebSocket server");

        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var endpoints = configuration.GetSection("Kestrel").GetSection("Endpoints").AsEnumerable();

        var endpoint = endpoints.Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => new Uri(x.Value!))
            .FirstOrDefault(new Uri("http://localhost:5000"));

        RemotePort = endpoint.Port;

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
            logger.LogInformation($"Received connection from {args.Client.IpPort} connected");

            if(string.IsNullOrEmpty(args.HttpRequest.Headers["kryo-client-id"])) 
            {
                logger.LogInformation("Received connection without client-id, forcing disconnect...");
                wsServer.DisconnectClient(args.Client.Guid);
                return;
            }

            if (!Guid.TryParse(args.HttpRequest.Headers["kryo-client-id"], out var guid)) 
            {
                logger.LogInformation("Received connection with invalid client-id, forcing disconnect...");
                wsServer.DisconnectClient(args.Client.Guid);
                return;
            }

            if (args.HttpRequest.Headers["X-Forwarded-For"] is null) 
            {
                // something went wrong, this should be set with builtin reverse proxy
                wsServer.DisconnectClient(args.Client.Guid);
                return;
            }

            Uri? url = null;

            if (!int.TryParse(args.HttpRequest.Headers["kryo-connect-to-port"], out var port))
            {
                port = 5000;
            }

            if (!Uri.TryCreate(args.HttpRequest.Headers["kryo-connect-to-url"], new UriCreationOptions(), out url))
            {
                var builder = new UriBuilder(args.HttpRequest.Headers["X-Forwarded-For"]!);
                builder.Port = port;
                url = builder.Uri;
            }

            var peer = new RemoteClient(wsServer, url, ServerId, args.Client.Guid);

            peer.LastSeen = DateTime.UtcNow;
            peer.ConnectedSince = DateTime.UtcNow;
            peer.ClientId = guid;

            Peers.TryAdd(args.Client.Guid, peer);

            ClientConnected?.Invoke(peer, args);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        wsServer.ClientDisconnected += (object? sender, DisconnectionEventArgs args) => {
            logger.LogInformation($"{args.Client.Ip} disconnected");
            Peers.TryRemove(args.Client.Guid, out var _);

            ClientDisconnected?.Invoke(sender, args);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        wsServer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
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
        };
    }

    public void Start()
    {
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

        logger.LogInformation($"Connecting to {url}");

        var peer = new LocalClient(url, configuration.GetValue<string>("PublicUrl"), RemotePort);

        peer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => 
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
            logger.LogInformation($"Connected to {url}");
            Peers.TryAdd(peer.ConnectionId, peer);
            ConnectedChanged?.Invoke(this, Peers.Count);
            return true;
        }

        return false;
    }

    public Dictionary<string, Peer> GetPeers()
    {
        return Peers.Values
            .Where(x => x is LocalClient)
            .ToDictionary(x => x.Url.ToString(), x => x);
    }

    public int GetLocalPort()
    {
        return LocalPort;
    }

    public int GetRemotePort()
    {
        return RemotePort;
    }
}
