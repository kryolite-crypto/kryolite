using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Kryolite.Shared;
using MessagePack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class MeshNetwork : IMeshNetwork
{
    public event EventHandler<PeerConnectedEventArgs>? PeerConnected;
    public event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    // TODO: better implementation
    public static event EventHandler<int>? ConnectedChanged;

    private ConcurrentDictionary<ulong, Peer> Peers = new();
    private MemoryCache cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private readonly IServer server;
    private readonly IConfiguration configuration;
    private readonly ILogger<MeshNetwork> logger;
    private readonly StartupSequence startup;
    
    public static MessagePackSerializerOptions lz4Options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray);

    private ulong serverId;
    private string networkName;
    private CancellationTokenSource tokenSource = new CancellationTokenSource();

    public MeshNetwork(IServer server, IConfiguration configuration, ILogger<MeshNetwork> logger, StartupSequence startup)
    {
        logger.LogInformation("Initializing WebSocket server");

        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));

        var path = Path.Join(BlockchainService.DATA_PATH, "node.id");

        if (!Path.Exists(path))
        {
            serverId = (ulong)Random.Shared.NextInt64();
            File.WriteAllText(path, serverId.ToString());
        }
        else
        {
            serverId = ulong.Parse(File.ReadAllText(path));
        }

        logger.LogInformation($"node.id = {serverId}");

        networkName = configuration.GetValue<string?>("NetworkName") ?? "MAINNET";
    }

    public async Task BroadcastAsync(IPacket packet)
    {
        try
        {
            var msg = new Message((uint)Random.Shared.NextInt64(), packet);
            var bytes = MessagePackSerializer.Serialize(msg, lz4Options);

            using(var _ = rwlock.EnterWriteLockEx())
            {
                cache.Set(msg.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            }

            await BroadcastAsync(bytes);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Broadcast failed");
        }
    }

    private async Task BroadcastAsync(Message msg)
    {
        try
        {
            var bytes = MessagePackSerializer.Serialize(msg, lz4Options);

            using(var _ = rwlock.EnterWriteLockEx())
            {
                cache.Set(msg.Id, string.Empty, DateTimeOffset.Now.AddHours(1));
            }

            await BroadcastAsync(bytes);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Broadcast failed");
        }
    }

    private async Task BroadcastAsync(byte[] bytes)
    {
        await Parallel.ForEachAsync(Peers, async (peer, token) => 
        {
            try
            {
                await peer.Value.SendAsync(bytes);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, $"Broadcast failed for {peer.Value.Uri.ToHostname()}");
            }
        });
    }

    public Dictionary<ulong, Peer> GetPeers()
    {
        return Peers.ToDictionary(x => x.Key, x => x.Value);
    }

    public List<Peer> GetOutgoingConnections()
    {
        using var _lock = rwlock.EnterReadLockEx();

        return Peers
            .Where(x => x.Value.ConnectionType == ConnectionType.OUT)
            .Select(x => x.Value)
            .ToList();
    }

    public ulong GetServerId()
    {
        return serverId;
    }


    // TODO: move to EndpointMaanager
    public List<Uri> GetEndpoints()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

        return addresses
            .Where(x => x is not null)
            .Select(x => new Uri(x))
            .ToList();
    }

    // TODO: move to EndpointMaanager
    private string GetPorts()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();
        return string.Join(",", addresses.Where(x => x is not null).Select(x => new Uri(x).Port));
    }

    public Peer? GetPeer(Uri uri)
    {
        return Peers.Where(x => x.Value.Uri == uri)
            .Select(x => x.Value)
            .FirstOrDefault();
    }

    public async Task<bool> ConnectToAsync(Uri uri)
    {
        var token = tokenSource.Token;
        var timer = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(5);

        if (Peers.Any(x => x.Value.Uri == uri))
        {
            return true;
        }

        var outgoing = GetOutgoingConnections();

        if (outgoing.Count >= Constant.MAX_PEERS)
        {
            var peer = outgoing.OrderBy(x => x.ConnectedSince)
                .First();

            logger.LogInformation($"Disconnecting from {peer.Uri}");

            await peer.DisconnectAsync();
        }

        try
        {
            while (timer.Elapsed < timeout)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var targetUri = new UriBuilder(uri)
                    {
                        Scheme = uri.Scheme == "https" ? "wss" : "ws",
                        Path = "/hive"
                    };

                    var client = new ClientWebSocket();

                    client.Options.KeepAliveInterval = TimeSpan.FromSeconds(60);
                    client.Options.SetRequestHeader("kryo-apilevel", Constant.API_LEVEL.ToString());
                    client.Options.SetRequestHeader("kryo-client-id", serverId.ToString());
                    client.Options.SetRequestHeader("kryo-network", networkName);
                    client.Options.SetRequestHeader("kryo-connect-to-url", configuration.GetValue<string>("PublicUrl"));
                    client.Options.SetRequestHeader("kryo-connect-to-ports", GetPorts());

                    await client.ConnectAsync(targetUri.Uri, token);

                    await Task.Delay(100);

                    if (client.State == WebSocketState.Open)
                    {
                        (var clientId, var apiLevel) = await DoHandshakeAsync(client, token);

                        if (clientId == serverId) 
                        {
                            logger.LogInformation($"Cancel connection to {targetUri.Uri}, self connection");
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                            return false;
                        }

                        if (apiLevel < Constant.MIN_API_LEVEL)
                        {
                            logger.LogInformation($"Cancel connection to {targetUri.Uri}, unsupported apilevel: {apiLevel}");
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                            return false;
                        }

                        if (Peers.ContainsKey(clientId))
                        {
                            logger.LogInformation($"Cancel connection to {targetUri.Uri}, already connected");
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                            return false;
                        }

                        logger.LogInformation($"Connected to {uri.ToHostname()}");

                        var peer = new Peer(client, clientId, uri, ConnectionType.OUT, true, apiLevel);

                        _ = AddSocketAsync(client, peer);

                        return true;
                    }
                }
                catch (WebSocketException wsEx)
                {
                    logger.LogInformation($"Error connecting to {uri.ToHostname()}: {wsEx.Message}");
                }

                token.WaitHandle.WaitOne(1000);
            }
        }
        catch (ConnectionClosedException ccEx)
        {
            logger.LogInformation($"{uri.ToHostname()} rejected connection, reason: {ccEx.CloseStatus}, {ccEx.Reason}");
        }
        catch (TaskCanceledException tcEx)
        {
            logger.LogDebug(tcEx, "Connection task cancelled");
        }
        catch (OperationCanceledException ocEx)
        {
            logger.LogDebug(ocEx, "Connection operation cancelled");
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Unknown error with websocket connection");
        }

        return false;
    }

    public async Task AddSocketAsync(WebSocket webSocket, Peer peer)
    {
        var token = tokenSource.Token;
        var buffer = new byte[64 * 1024];

        if(Peers.TryAdd(peer.ClientId, peer))
        {
            if (peer.ConnectionType == ConnectionType.IN && peer.IsReachable)
            {
                var discovery = new NodeDiscovery(peer.Uri);
                var msg = new Message(peer.ClientId, discovery);

                await BroadcastAsync(msg);
            }
        }

        _ = Task.Run(() => PeerConnected?.Invoke(peer, new PeerConnectedEventArgs()));
        _ = Task.Run(() => ConnectedChanged?.Invoke(this, Peers.Count));

        try
        {
            while (!token.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(webSocket, buffer, token);

                if (message == null)
                {
                    continue;
                }

                if (message.MessageType == WebSocketMessageType.Text)
                {
                    await ReplyHandshakeAsync(webSocket, message, token);
                    continue;
                }

                _ = Task.Run(async () => {
                    var messageArgs = new MessageReceivedEventArgs(message);

                    using(var _ = rwlock.EnterWriteLockEx())
                    {
                        if(cache.TryGetValue(messageArgs.Message.Id, out var _))
                        {
                            return;
                        }

                        cache.Set(messageArgs.Message.Id, peer.ClientId, DateTimeOffset.Now.AddMinutes(30));
                    }

                    peer.LastSeen = DateTime.UtcNow;
                    MessageReceived?.Invoke(peer, messageArgs);

                    if (messageArgs.Rebroadcast)
                    {
                        await BroadcastAsync(message.Bytes.Array!);
                    }
                });
            }
        }
        catch (ConnectionClosedException ccEx)
        {
            logger.LogInformation($"{peer.Uri.ToHostname()} closed connection, reason: {ccEx.CloseStatus}, {ccEx.Reason}");
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "WebSocket connection threw error");
        }

        Peers.TryRemove(peer.ClientId, out _);

        _ = Task.Run(() => PeerDisconnected?.Invoke(peer, new PeerDisconnectedEventArgs(webSocket.CloseStatus ?? WebSocketCloseStatus.Empty)));
        _ = Task.Run(() => ConnectedChanged?.Invoke(this, Peers.Count));
    }

    private async Task ReplyHandshakeAsync(WebSocket webSocket, RawMessage message, CancellationToken token)
    {
        var text = Encoding.UTF8.GetString(message.Bytes.Where(x => x != 0).ToArray());

        if (text == "HI")
        {
            var bytes = Encoding.UTF8.GetBytes($"{serverId};{Constant.API_LEVEL}");
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }
    }

    private async Task<(ulong, int)> DoHandshakeAsync(ClientWebSocket client, CancellationToken token)
    {
        var buffer = new byte[8 * 1024];

        await client.SendAsync(Encoding.UTF8.GetBytes("HI"), WebSocketMessageType.Text, true, token);

        var message = await ReadMessageAsync(client, buffer, token);

        if (message?.MessageType != WebSocketMessageType.Text)
        {
            throw new Exception("handshake failed, clientId not received");
        }

        var parts = Encoding.UTF8.GetString(message.Bytes).Split(";");

        if (parts.Length != 2)
        {
            throw new Exception("handshake failed, invalid reply received");
        }

        var id = parts[0];
        var apiLevelStr = parts[1];

        if (!ulong.TryParse(id, out var clientId))
        {
            throw new Exception("handshake failed, clientId invalid format");
        }

        if (!int.TryParse(apiLevelStr, out var apiLevel))
        {
            throw new Exception("handshake failed, apilevel invalid format");
        }

        return (clientId, apiLevel);
    }

    private async Task<RawMessage?> ReadMessageAsync(WebSocket webSocket, byte[] buffer, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var segment = new ArraySegment<byte>(buffer);

        while (!token.IsCancellationRequested)
        {
            if (webSocket.State != WebSocketState.Open)
            {
                throw new Exception($"WebSocketState = {webSocket.State}");
            }

            var result = await webSocket.ReceiveAsync(segment, token);

            if (result.CloseStatus != null)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye!", CancellationToken.None);
                throw new ConnectionClosedException(result.CloseStatus, result.CloseStatusDescription);
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                return new RawMessage(result.MessageType, stream.GetBuffer());
            }
        }

        return null;
    }
}

public class PeerConnectedEventArgs
{
    public PeerConnectedEventArgs()
    {

    }
}

public class PeerDisconnectedEventArgs
{
    public WebSocketCloseStatus CloseStatus { get; }

    public PeerDisconnectedEventArgs(WebSocketCloseStatus closeStatus)
    {
        CloseStatus = closeStatus;
    }
}

public class MessageReceivedEventArgs
{
    public Message Message{ get; }
    public WebSocketMessageType MessageType { get; }
    public bool Rebroadcast { get; set; }

    public MessageReceivedEventArgs(RawMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        Message = MessagePackSerializer.Deserialize<Message>(message.Bytes, MeshNetwork.lz4Options);
        MessageType = message.MessageType;
    }
}

public class RawMessage
{
    public ArraySegment<byte> Bytes { get; }
    public WebSocketMessageType MessageType { get; }

    public RawMessage(WebSocketMessageType messageType, ArraySegment<byte> bytes)
    {
        MessageType = messageType;
        Bytes = bytes;
    }
}

public class ConnectionClosedException : Exception
{
    public WebSocketCloseStatus? CloseStatus { get; }
    public string? Reason { get; }

    public ConnectionClosedException(WebSocketCloseStatus? status, string? reason)
    {
        CloseStatus = status;
        Reason = reason;
    }
}