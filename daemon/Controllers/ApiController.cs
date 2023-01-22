using Kryolite.Node;

namespace Kryolite.Daemon;

public class ApiController : ApiControllerBase
{
    public ApiController(IBlockchainManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork) : base(blockchainManager, networkManager, meshNetwork)
    {
    }
}
