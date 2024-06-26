using Kryolite.Grpc.NodeService;
using Kryolite.Shared;
using Kryolite.Transport.Websocket;
using Kryolite.Type;

namespace Kryolite.Node.Network;

public interface IConnectionManager
{
    event EventHandler<NodeConnection>? NodeConnected;
    event EventHandler<NodeConnection>? NodeDisconnected;

    List<NodeConnection> GetConnectedNodes();
    Task StartListening(Uri uri, PublicKey publicKey, WebsocketChannel channel, string version);

    INodeService CreateClient(NodeConnection connection);
}
