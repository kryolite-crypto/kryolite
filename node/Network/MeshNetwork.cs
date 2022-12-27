using System.Collections.Concurrent;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WatsonWebsocket;

namespace Kryolite.Node;

public class MeshNetwork : IMeshNetwork
{
    public int Port { get; init; }

    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
    public event EventHandler? ClientDropped;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    
    // TODO: better implementation
    public static event EventHandler<int>? ConnectedChanged;
    public static Guid ServerId { get; } = Guid.NewGuid();

    private WatsonWsServer wsServer;
    private ConcurrentDictionary<string, BaseNode> Peers = new();
    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly IConfiguration configuration;
    private readonly ILogger<MeshNetwork> logger;
    private MessagePackSerializerOptions lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

    public MeshNetwork(IConfiguration configuration, ILogger<MeshNetwork> logger)
    {
        logger.LogInformation("Initializing WebSocket server");

        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var ip = configuration.GetValue<string>("NodeIp");
        var port = configuration.GetValue<int>("NodePort");

        wsServer = new WatsonWsServer(ip, port, false);
        Port = port;

        wsServer.ClientConnected += (object? sender, ClientConnectedEventArgs args) => {
            var peer = new Client(wsServer, args.IpPort, ServerId);

            if(string.IsNullOrEmpty(args.HttpRequest.Headers["ClientId"])) {
                wsServer.DisconnectClient(args.IpPort);
                return;
            }

            if (!Guid.TryParse(args.HttpRequest.Headers["ClientId"], out var guid)) {
                wsServer.DisconnectClient(args.IpPort);
                return;
            }

            peer.LastSeen = DateTime.UtcNow;
            peer.ConnectedSince = DateTime.UtcNow;
            peer.ClientId = guid;

            Peers.TryAdd(args.IpPort, peer);

            ClientConnected?.Invoke(sender, args);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        wsServer.ClientDisconnected += (object? sender, ClientDisconnectedEventArgs args) => {
            Peers.TryRemove(args.IpPort, out var _);

            ClientDisconnected?.Invoke(sender, args);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        wsServer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
            var eventArgs = new MessageEventArgs(args.Data, lz4Options);

            eventArgs.Hostname = args.IpPort;

            using(var _ = rwlock.EnterWriteLockEx())
            {
                if(cache.TryGetValue(eventArgs.Message.Id, out var _)) {
                    return;
                }

                cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            }
            
            if (Peers.TryGetValue(args.IpPort, out var peer))
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

    public async Task<bool> AddNode(string hostname, bool ssl, Guid clientId)
    {
        try
        {
            if (!hostname.StartsWith("http://"))
            {
                hostname = $"http://{hostname}";
            }

            var uri = new Uri(hostname);
            return await AddNode(uri.Host, uri.Port, ssl, clientId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"Hostname: {hostname}");
            return false;
        }
    }

    public async Task<bool> AddNode(string hostname, int port, bool ssl, Guid clientId)
    {
        string ipAndPort = $"{hostname}:{port}";

        using(var _ = rwlock.EnterWriteLockEx())
        {
            if (Peers.ContainsKey(ipAndPort))
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

        logger.LogInformation($"Connecting to {ipAndPort}");

        var peer = new Peer(hostname, port, ssl, Port);

        peer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => 
        {
            var eventArgs = new MessageEventArgs(args.Data, lz4Options);

            eventArgs.Hostname = args.IpPort;

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
            Peers.TryRemove(ipAndPort, out var _);

            ClientDropped?.Invoke(sender, EventArgs.Empty);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        if (await peer.StartWithTimeoutAsync())
        {
            logger.LogInformation($"Connected to {ipAndPort}");
            Peers.TryAdd(ipAndPort, peer);
            ConnectedChanged?.Invoke(this, Peers.Count);
            return true;
        }

        return false;
    }

    public Dictionary<string, Guid> GetPeers()
    {
        return Peers.Values
            .Where(x => x is Peer)
            .ToDictionary(x => $"http://{x.Hostname}:{x.Port}", x => x.ClientId);
    }

    public int GetPort()
    {
        return Port;
    }
}
