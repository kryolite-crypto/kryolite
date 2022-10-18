using static Marccacoin.NetworkManager;

namespace Marccacoin;

public interface INetworkManager
{
    void AddHost(NodeHost host);
    int GetHostCount();

    DateTimeOffset GetNetworkTime();
}
