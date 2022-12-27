using Kryolite.Shared;
using MessagePack;
using WatsonWebsocket;

namespace Kryolite;

public abstract class BaseNode
{
    public string Hostname { get; protected set; } = string.Empty;
    public int Port { get; protected set; }
    public DateTime LastSeen { get; set; }
    public DateTime ConnectedSince { get; set; }
    public List<PosBlock> Blockchain { get; set; } = new List<PosBlock>();
    public Guid ClientId;

    public abstract Task SendAsync(Message msg);
    public abstract void Disconnect();
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
        try
        {
            msg.NodeId = serverId;

            var bytes = MessagePackSerializer.Serialize(msg);
            await watsonServer.SendAsync(ipAndPort, bytes);
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
            if (watsonServer.IsClientConnected(ipAndPort))
            {
                watsonServer.DisconnectClient(ipAndPort);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}


public class Peer : BaseNode
{
    public bool ForceDisconnect { get; set; }

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<EventArgs>? Dropped;

    private WatsonWsClient wsClient;

    public Peer(string hostname, int port, bool ssl, int serverPort)
    {
        Hostname = hostname;
        Port = port;

        wsClient = new WatsonWsClient(hostname, port, ssl);

        wsClient.ConfigureOptions(opts => {
            opts.SetRequestHeader("ClientId", Kryolite.Node.MeshNetwork.ServerId.ToString());
        });

        wsClient.MessageReceived += (object? sender, MessageReceivedEventArgs args) => {
            if (ClientId == null) {
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
