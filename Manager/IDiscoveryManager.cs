using static Marccacoin.DiscoveryManager;

namespace Marccacoin;

public interface IDiscoveryManager
{
    void AddHost(Host host);
    DateTimeOffset GetNetworkTime();
}
