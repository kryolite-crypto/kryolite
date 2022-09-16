using Marccacoin;

namespace volvi_wallet;

public class ApiController : ApiControllerBase
{
    public ApiController(IBlockchainManager blockchainManager) : base(blockchainManager)
    {
    }
}
