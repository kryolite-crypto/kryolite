using Marccacoin.Shared;
using MessagePack;
using WatsonWebsocket;

namespace Marccacoin;

public class Node
{
    public string Hostname { get; }
    public int Port { get; }

    public DateTime LastSeen { get; set; }
    public List<Block> Blockchain { get; set; } = new List<Block>();

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<EventArgs>? Dropped;

    private WatsonWsClient wsClient;
    private Guid ServerId;

    public Node(string hostname, int port, bool ssl, Guid serverId, int serverPort)
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
            for (int i = 1; i <= 10; i++) {
                // TODO: Logger
                Console.WriteLine($"Reconnecting to {hostname}:{port} ({i} / {10})");

                if (await wsClient.StartWithTimeoutAsync()) {
                    break;
                }
            }

            Dropped?.Invoke(this, EventArgs.Empty);
            wsClient.Dispose();
        };

        wsClient.Start();
    }

    public async Task SendAsync(Message msg)
    {
        msg.NodeId = ServerId;

        var bytes = MessagePackSerializer.Serialize(msg);
        await wsClient.SendAsync(bytes);
    }
}
