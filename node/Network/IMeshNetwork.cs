using WatsonWebsocket;

namespace Kryolite.Node;

public interface IMeshNetwork
{
    string? PublicAddress { get; set; }

    void Start();
    Task BroadcastAsync(Message msg);
    Task<bool> AddNode(Uri host, Guid clientId);
    Dictionary<string, Peer> GetPeers();
    int GetLocalPort();
    List<Uri> GetEndpoints();

    event EventHandler<ConnectionEventArgs>? ClientConnected;
    event EventHandler<DisconnectionEventArgs>? ClientDisconnected;
    event EventHandler? ClientDropped;
    event EventHandler<MessageEventArgs>? MessageReceived;
}
