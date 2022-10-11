using Marccacoin.Shared;
using MessagePack;
using WatsonWebsocket;

namespace Marccacoin;

public abstract class Node
{
    public string Hostname { get; protected set; }
    public int Port { get; protected set; }
    public DateTime LastSeen { get; set; }
    public List<Block> Blockchain { get; set; } = new List<Block>();

    public abstract Task SendAsync(Message msg);
}

public class Client : Node
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


public class Peer : Node
{
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<EventArgs>? Dropped;

    private WatsonWsClient wsClient;
    private Guid ServerId;

    public Peer(string hostname, int port, bool ssl, Guid serverId, int serverPort)
    {
        Hostname = hostname;
        Port = port;

        ServerId = serverId;
        wsClient = new WatsonWsClient(hostname, port, ssl);

        wsClient.ConfigureOptions(opts => {
            opts.SetRequestHeader("ClientId", serverId.ToString());
        });

        wsClient.MessageReceived += (object? sender, MessageReceivedEventArgs args) => {
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

        wsClient.ServerDisconnected += async (object? sender, EventArgs e) => {
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
        msg.NodeId = ServerId;

        var bytes = MessagePackSerializer.Serialize(msg);
        await wsClient.SendAsync(bytes);
    }
}
