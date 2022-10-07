using System.Collections.Concurrent;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using WatsonWebsocket;

namespace Marccacoin;

public class Network
{
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
    public event EventHandler<MessageEventArgs>? MessageReceived;

    private Guid ServerId = Guid.NewGuid();
    private WatsonWsServer wsServer;

    private ConcurrentDictionary<Guid, string> Clients = new ConcurrentDictionary<Guid, string>();
    private List<Node> Peers = new List<Node>();
    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());

    public Network(string ip, int port, bool ssl)
    {
        wsServer = new WatsonWsServer(ip, port, ssl);

        wsServer.ClientConnected += (object? sender, ClientConnectedEventArgs args) => {
            Clients.AddOrUpdate(Guid.Parse(args.HttpRequest.Headers["ClientID"]!), args.IpPort, (x, y) => y);
            ClientConnected?.Invoke(sender, args);
        };

        wsServer.ClientDisconnected += (object? sender, ClientDisconnectedEventArgs args) => {
            foreach (var peer in Clients) {
                if (peer.Value == args.IpPort) {
                    Clients.TryRemove(peer);
                }
            }

            ClientDisconnected?.Invoke(sender, args);
        };

        wsServer.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
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

        wsServer.Start();
    }

    public async Task BroadcastAsync(Message msg)
    {
        if (msg.NodeId == null) {
            msg.NodeId = ServerId;
        }

        var bytes = MessagePackSerializer.Serialize(msg);

        var tClients = Parallel.ForEachAsync(Clients, async (client, token) => {
            if (client.Key != ServerId || client.Key == msg.NodeId) {
                await wsServer.SendAsync(client.Value, bytes);
            }
        });

        var tPeers = Parallel.ForEachAsync(Peers, async (peer, token) => {
            await peer.SendAsync(msg);
        });

        await Task.WhenAll(tClients, tPeers);
    }

    public async Task SendAsync(Guid id, Message msg)
    {
        var bytes = MessagePackSerializer.Serialize(msg);

        if (Clients.TryGetValue(id, out var ip)) {
            await wsServer.SendAsync(ip, bytes);
        }
    }

    public Node AddNode(string ip, int port, bool ssl)
    {
        var node = new Node(ip, port, ssl, ServerId);

        node.MessageReceived += async (object? sender, MessageReceivedEventArgs args) => {
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

        Peers.Add(node);

        return node;
    }
}
