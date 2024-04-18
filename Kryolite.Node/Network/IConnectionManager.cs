using Kryolite.Grpc.NodeService;

namespace Kryolite.Node.Network;

public interface IConnectionManager
{
    event EventHandler<NodeConnection>? NodeConnected;
    event EventHandler<NodeConnection>? NodeDisconnected;
   
    List<NodeConnection> GetConnectedNodes();
    INodeService CreateClient(NodeConnection connection);
    INodeService CreateClient(Node node);
}
