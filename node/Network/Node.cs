using Kryolite.Shared;
using MessagePack;
using WatsonWebsocket;

namespace Kryolite;

public abstract class Peer
{
    public Uri Url { get; protected set; }
    public DateTime LastSeen { get; set; }
    public DateTime ConnectedSince { get; set; }
    public List<PosBlock> Blockchain { get; set; } = new List<PosBlock>();
    public Guid ClientId = Guid.Empty;

    public Peer(Uri url)
    {
        Url = url;
    }

    public abstract Task SendAsync(Message msg);
    public abstract void Disconnect();
}

public class RemoteClient : Peer
{
    WatsonWsServer watsonServer;
    private readonly Guid serverId;
    private readonly Guid connectionId;

    public RemoteClient(WatsonWsServer watsonServer, Uri client, Guid serverId, Guid connectionId) : base(client)
    {
        this.watsonServer = watsonServer ?? throw new ArgumentNullException(nameof(watsonServer));
        this.serverId = serverId;
        this.connectionId = connectionId;
    }

    public override async Task SendAsync(Message msg)
    {
        try
        {
            msg.NodeId = serverId;

            var bytes = MessagePackSerializer.Serialize(msg);
            await watsonServer.SendAsync(connectionId, bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public override void Disconnect()
    {
        try
        {
            if (watsonServer.IsClientConnected(connectionId))
            {
                watsonServer.DisconnectClient(connectionId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}


public class LocalClient : Peer
{
    public bool ForceDisconnect { get; set; }
    public Guid ConnectionId { get; set; }

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<EventArgs>? Dropped;

    private WatsonWsClient wsClient;

    public LocalClient(Uri client, string? publicUrl, int serverPort) : base(client)
    {
        var url = new UriBuilder(client);
        url.Scheme = client.Scheme == Uri.UriSchemeHttps ?
            Uri.UriSchemeWss : Uri.UriSchemeWs;

        wsClient = new WatsonWsClient(url.Uri);

        wsClient.ConfigureOptions(opts => {
            opts.SetRequestHeader("kryo-client-id", Kryolite.Node.MeshNetwork.ServerId.ToString());

            if (!string.IsNullOrEmpty(publicUrl))
            {
                opts.SetRequestHeader("kryo-connect-to-url", publicUrl);
            }
            else
            {
                opts.SetRequestHeader("kryo-connect-to-port", serverPort.ToString());
            }
        });

        wsClient.MessageReceived += (object? sender, MessageReceivedEventArgs args) => {
            if (ClientId == Guid.Empty) {
                ClientId = MessagePackSerializer.Deserialize<Message>(args.Data).NodeId;
            }

            LastSeen = DateTime.UtcNow;
            MessageReceived?.Invoke(this, args);
        };

        wsClient.ServerConnected += async (object? sender, EventArgs e) => {
            LastSeen = DateTime.UtcNow;
            ConnectedSince = DateTime.UtcNow;

            var msg = new Message
            {
                Payload = new QueryNodeInfo()
            };

            await SendAsync(msg);
        };

        wsClient.ServerDisconnected += (object? sender, EventArgs e) => {
            // TODO: Logger
            Console.WriteLine($"Disconnected from {Url}");

            Dropped?.Invoke(this, EventArgs.Empty);
            wsClient.Dispose();
        };
    }

    public async Task<bool> StartWithTimeoutAsync()
    {
        try
        {
            return await wsClient.StartWithTimeoutAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return false;
        }
    }

    public override async Task SendAsync(Message msg)
    {
        try
        {
            msg.NodeId = Kryolite.Node.MeshNetwork.ServerId;

            var bytes = MessagePackSerializer.Serialize(msg);
            var res = await wsClient.SendAsync(bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public override void Disconnect()
    {
        try
        {
            ForceDisconnect = true;
            wsClient.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}
