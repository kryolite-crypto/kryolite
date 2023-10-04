using Kryolite.Node;
using Kryolite.Node.Services;
using Kryolite.Shared.Dto;

namespace Kryolite.Wallet;

public class ApiController : ApiControllerBase
{
    public ApiController(IStoreManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork) : base(blockchainManager, networkManager, meshNetwork)
    {
    }
}
