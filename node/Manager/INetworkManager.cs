using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;

namespace Kryolite.Node;

public interface INetworkManager
{
    void AddHost(NetworkManager.NodeHost host);
    //void AddHosts(List<NetworkManager.NodeHost> hosts);
    int GetHostCount();
    List<NetworkManager.NodeHost> GetHosts();
    DateTimeOffset GetNetworkTime();
    bool ProposeBlock(PowBlock block);
    IDisposable OnBlockProposed(ITargetBlock<PowBlock> action);
}
