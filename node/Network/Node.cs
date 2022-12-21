using Kryolite.Shared;
using MessagePack;
using WatsonWebsocket;

namespace Kryolite;

public abstract class BaseNode
{
    public string Hostname { get; protected set; } = string.Empty;
    public int Port { get; protected set; }
    public DateTime LastSeen { get; set; }
    public List<PosBlock> Blockchain { get; set; } = new List<PosBlock>();
    public Guid ClientId;

    public abstract Task SendAsync(Message msg);
}

public class Client : BaseNode
{
    WatsonWsServer watsonServer;
    private readonly string ipAndPort;
    private readonly Guid serverId;

    public Client(WatsonWsServer watsonServer, string ipAndPort, Guid serverId)
    {
        if (string.IsNullOrEmpty(ipAndPort))
        {
            throw new ArgumentException($"'{nameof(ipAndPort)}' cannot be null or empty.", nameof(ipAndPort));
        }

        this.watsonServer = watsonServer ?? throw new ArgumentNullException(nameof(watsonServer));
        this.ipAndPort = ipAndPort;
        this.serverId = serverId;

        var uri = new Uri($"http://{ipAndPort}");
        Hostname = uri.Host;
        Port = uri.Port;
    }

    public override async Task SendAsync(Message msg)
    {
        msg.NodeId = serverId;

        var bytes = MessagePackSerializer.Serialize(msg);
        await watsonServer.SendAsync(ipAndPort, bytes);
    }
}


public class Peer : BaseNode
{
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<EventArgs>? Dropped;

    private WatsonWsClient wsClient;

    public Peer(string hostname, int port, bool ssl, int serverPort)
    {
        Hostname = hostname;
        Port = port;

        wsClient = new WatsonWsClient(hostname, port, ssl);

        wsClient.ConfigureOptions(opts => {
            opts.SetRequestHeader("ClientId", Kryolite.Node.Network.ServerId.ToString());
        });

        wsClient.MessageReceived += (object? sender, MessageReceivedEventArgs args) => {
            if (ClientId == null) {
                ClientId = MessagePackSerializer.Deserialize<Message>(args.Data).NodeId;
            }

            LastSeen = DateTime.UtcNow;
            MessageReceived?.Invoke(this, args);
        };

        wsClient.ServerConnected += async (object? sender, EventArgs e) => {
            // TODO: Logger
            Console.WriteLine($"Connected to {hostname}:{port}");

            LastSeen = DateTime.UtcNow;

            var msg = new Message
            {
                Payload = new QueryNodeInfo 
                {
                    Port = serverPort
                }
            };

            await SendAsync(msg);
        };

        wsClient.ServerDisconnected += (object? sender, EventArgs e) => {
            // TODO: Logger
            Console.WriteLine($"Disconnected from {hostname}:{port}");

            Dropped?.Invoke(this, EventArgs.Empty);
            wsClient.Dispose();
        };
    }

    public Task<bool> StartWithTimeoutAsync()
    {
        return wsClient.StartWithTimeoutAsync();
    }

    public override async Task SendAsync(Message msg)
    {
        msg.NodeId = Kryolite.Node.Network.ServerId;

        var bytes = MessagePackSerializer.Serialize(msg);
        var res = await wsClient.SendAsync(bytes);
    }
}
