using Marccacoin;

namespace holvi_wallet;

public class ApiController : ApiControllerBase
{
    public ApiController(IBlockchainManager blockchainManager, INetworkManager networkManager) : base(blockchainManager, networkManager)
    {
    }
}
