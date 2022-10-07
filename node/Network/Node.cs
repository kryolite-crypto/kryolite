using Marccacoin.Shared;
using MessagePack;
using WatsonWebsocket;

namespace Marccacoin;

public class Node
{
    //public Uri NodeUri { get; set; }
    public DateTime LastSeen { get; set; }
    public List<Block> Blockchain { get; set; } = new List<Block>();

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    private WatsonWsClient wsClient;
    private Guid ServerId;

    public Node(string ip, int port, bool ssl, Guid serverId)
    {
        ServerId = serverId;
        wsClient = new WatsonWsClient(ip, port, ssl);

        wsClient.ConfigureOptions(opts => {
            opts.SetRequestHeader("ClientId", serverId.ToString());
        });

        wsClient.MessageReceived += (object? sender, MessageReceivedEventArgs args) => {
            LastSeen = DateTime.UtcNow;
            MessageReceived?.Invoke(this, args);
        };

        wsClient.ServerConnected += async (object? sender, EventArgs e) => {
            LastSeen = DateTime.UtcNow;

            var msg = new Message
            {
                Payload = new Query 
                {
                    QueryType = QueryType.NODE_INFO
                }
            };

            await SendAsync(msg);
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
