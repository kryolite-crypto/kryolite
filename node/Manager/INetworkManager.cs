using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;

namespace Kryolite.Node;

public interface INetworkManager
{
    void AddHost(NetworkManager.NodeHost host);
    //void AddHosts(List<NetworkManager.NodeHost> hosts);
    int GetHostCount();
    List<NetworkManager.NodeHost> GetHosts();
    void RemoveHost (NetworkManager.NodeHost host);
}
