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

    private Guid ServerId = Guid.NewGuid();
    private WatsonWsServer wsServer;

    private ConcurrentDictionary<string, Node> Peers = new ConcurrentDictionary<string, Node>();
    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());

    public Network(string ip, int port, bool ssl)
    {
        wsServer = new WatsonWsServer(ip, port, ssl);
        Port = port;

        wsServer.ClientConnected += (object? sender, ClientConnectedEventArgs args) => {
            ClientConnected?.Invoke(sender, args);

            var peer = new Client(wsServer, args.IpPort, ServerId);
            Peers.TryAdd(args.IpPort, peer);
        };

        wsServer.ClientDisconnected += (object? sender, ClientDisconnectedEventArgs args) => {
            Peers.TryRemove(args.IpPort, out var _);

            ClientDisconnected?.Invoke(sender, args);
        };

        wsServer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
            var eventArgs = new MessageEventArgs(args.Data);

            eventArgs.Hostname = args.IpPort.Split(":").First();

            if(cache.TryGetValue(eventArgs.Message.Id, out var _)) {
                return;
            }

            cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            
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

            if(cache.TryGetValue(eventArgs.Message.Id, out var _)) {
                return;
            }

            cache.Set(eventArgs.Message.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            
            MessageReceived?.Invoke(sender, eventArgs);
            
            if (eventArgs.Rebroadcast) {
                await BroadcastAsync(eventArgs.Message);
            }
        };

        peer.Dropped += (object? sender, EventArgs args) => {
            Peers.TryRemove(ipAndPort, out var _);
            ClientDropped?.Invoke(sender, EventArgs.Empty);
        };

        for (int i = 1; i <= 10; i++)
        {
            Console.WriteLine($"{i}/{10}: Connecting to {ipAndPort}");
            if(await peer.StartWithTimeoutAsync()) {
                Peers.TryAdd(ipAndPort, peer);
                return true;
            }
        }

        return false;
    }
}
