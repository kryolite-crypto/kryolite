using Kryolite.Node;

namespace Kryolite.Wallet;

public class ApiController : ApiControllerBase
{
    public ApiController(IBlockchainManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork) : base(blockchainManager, networkManager, meshNetwork)
    {
    }
}
