namespace Kryolite.Node.Network;

public interface IConnectionManager
{
    event EventHandler<NodeConnection>? NodeConnected;
    event EventHandler<NodeConnection>? NodeDisconnected;

    
    List<NodeConnection> GetConnectedNodes();
    T CreateClient<T>(NodeConnection connection) where T : class;
    T CreateClient<T>(Node node) where T : class;
}
