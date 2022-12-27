using WatsonWebsocket;

namespace Kryolite.Node;

public interface IMeshNetwork
{
    void Start();
    Task BroadcastAsync(Message msg);
    Task<bool> AddNode(string hostname, bool ssl, Guid clientId);
    Task<bool> AddNode(string hostname, int port, bool ssl, Guid clientId);
    Dictionary<string, Guid> GetPeers();
    int GetPort();

    event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
    event EventHandler? ClientDropped;
    event EventHandler<MessageEventArgs>? MessageReceived;
}