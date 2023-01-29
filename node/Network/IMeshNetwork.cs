using System.Net.WebSockets;

namespace Kryolite.Node;

public interface IMeshNetwork
{
    Task BroadcastAsync(IPacket packet);
    Task<bool> ConnectToAsync(Uri uri);
    Task AddSocketAsync(WebSocket webSocket, Peer peer);

    List<Uri> GetEndpoints();
    Dictionary<ulong, Peer> GetPeers();
    List<Peer> GetOutgoingConnections();
    ulong GetServerId();
    Peer? GetPeer(Uri uri);

    event EventHandler<PeerConnectedEventArgs>? PeerConnected;
    event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
}
