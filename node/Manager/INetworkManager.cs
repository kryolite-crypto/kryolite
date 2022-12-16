using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using static Marccacoin.NetworkManager;

namespace Marccacoin;

public interface INetworkManager
{
    void AddHost(NodeHost host);
    int GetHostCount();
    List<NodeHost> GetHosts();
    DateTimeOffset GetNetworkTime();
    bool ProposeBlock(PowBlock block);
    IDisposable OnBlockProposed(ITargetBlock<PowBlock> action);
}
