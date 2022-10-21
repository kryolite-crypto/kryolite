using System.Collections.Concurrent;
using Marccacoin.Shared;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using WatsonWebsocket;

namespace Marccacoin;

public class Network
{
    public int Port { get; init; }

    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
    public event EventHandler? ClientDropped;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    
    // TODO: better implementation
    public static event EventHandler<int>? ConnectedChanged;


    private Guid ServerId = Guid.NewGuid();
    private WatsonWsServer wsServer;

    private ConcurrentDictionary<string, Node> Peers = new();
    //private ConcurrentDictionary<Guid, Node> Peers = new();

    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    public Network(string ip, int port, bool ssl)
    {
        wsServer = new WatsonWsServer(ip, port, ssl);
        Port = port;

        wsServer.ClientConnected += (object? sender, ClientConnectedEventArgs args) => {
            // TODO: Check clientid against serverid
            var peer = new Client(wsServer, args.IpPort, ServerId);
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

        await Parallel.ForEachAsync(Peers, async (peer, token) => {
            await peer.Value.SendAsync(msg);
        });
    }

    public async Task<bool> AddNode(string hostname, bool ssl)
    {
        var uri = new Uri(hostname);
        return await AddNode(uri.Host, uri.Port, ssl);
    }

    public async Task<bool> AddNode(string hostname, int port, bool ssl)
    {
        string ipAndPort = $"{hostname}:{port}";

        if (Peers.Count == Constant.MAX_PEERS) {
            return false;
        }

        if (Peers.ContainsKey(ipAndPort)) 
        {
            return false;
        }

        var peer = new Peer(hostname, port, ssl, ServerId, Port);

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

    public List<string> GetPeers()
    {
        return Peers.Values
            .Where(x => x is Peer).Select(x => $"http://{x.Hostname}:{x.Port}")
            .ToList();
    }
}
