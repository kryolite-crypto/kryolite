namespace Marccacoin;

public class ApiController : ApiControllerBase
{
    public ApiController(IBlockchainManager blockchainManager, INetworkManager networkManager) : base(blockchainManager, networkManager)
    {
    }
}
