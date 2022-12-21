using System.Collections.Concurrent;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using WatsonWebsocket;

namespace Kryolite.Node;

public class Network
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

    public Network(string ip, int port, bool ssl)
    {
        wsServer = new WatsonWsServer(ip, port, ssl);
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
            var eventArgs = new MessageEventArgs(args.Data);

            eventArgs.Hostname = args.IpPort.Split(":").First();

            using(var _ = rwlock.EnterWriteLockEx())
            {
                if(cache.TryGetValue(eventArgs.Message.Id, out var _)) {
                    return;
                }

                cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            }
            
            if (Peers.TryGetValue(args.IpPort, out var peer))
            {
                MessageReceived?.Invoke(peer, eventArgs);
            }
            
            if (eventArgs.Rebroadcast) {
                await BroadcastAsync(eventArgs.Message);
            }
        };

        wsServer.Start();
    }

    public async Task BroadcastAsync(Message msg)
    {
        if (msg.NodeId == null) {
            msg.NodeId = ServerId;
        }

        var bytes = MessagePackSerializer.Serialize(msg);

        using(var _ = rwlock.EnterWriteLockEx())
        {
            cache.Set(msg.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
        }

        await Parallel.ForEachAsync(Peers, async (peer, token) => {
            await peer.Value.SendAsync(msg);
        });
    }

    public async Task<bool> AddNode(string hostname, bool ssl, Guid clientId)
    {
        var uri = new Uri(hostname);
        return await AddNode(uri.Host, uri.Port, ssl, clientId);
    }

    public async Task<bool> AddNode(string hostname, int port, bool ssl, Guid clientId)
    {
        string ipAndPort = $"{hostname}:{port}";

        using(var _ = rwlock.EnterWriteLockEx())
        {
            if (Peers.Count == Constant.MAX_PEERS) {
                return false;
            }

            if (Peers.ContainsKey(ipAndPort)) 
            {
                return false;
            }

            if (Peers.Values.Any(x => x.ClientId == clientId))
            {
                return false;
            }
        }

        var peer = new Peer(hostname, port, ssl, Port);

        peer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
            var eventArgs = new MessageEventArgs(args.Data);

            using(var _ = rwlock.EnterWriteLockEx())
            {
                if(cache.TryGetValue(eventArgs.Message.Id, out var _)) {
                    return;
                }

                cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            }
            
            MessageReceived?.Invoke(sender, eventArgs);
            
            if (eventArgs.Rebroadcast) {
                await BroadcastAsync(eventArgs.Message);
            }
        };

        peer.Dropped += (object? sender, EventArgs args) => {
            Peers.TryRemove(ipAndPort, out var _);

            ClientDropped?.Invoke(sender, EventArgs.Empty);
            ConnectedChanged?.Invoke(this, Peers.Count);
        };

        if (await peer.StartWithTimeoutAsync())
        {
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
}
