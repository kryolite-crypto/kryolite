using Kryolite.Node;

namespace Kryolite.Daemon;

public class ApiController : ApiControllerBase
{
    public ApiController(IBlockchainManager blockchainManager, INetworkManager networkManager) : base(blockchainManager, networkManager)
    {
    }
}
