using static Marccacoin.NetworkManager;

namespace Marccacoin;

public interface IDiscoveryManager
{
    void AddHost(NodeHost host);
    DateTimeOffset GetNetworkTime();
}
