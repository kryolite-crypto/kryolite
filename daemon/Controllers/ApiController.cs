using Kryolite.Node;
using Kryolite.Node.Services;
using Kryolite.Shared.Dto;

namespace Kryolite.Daemon;

public class ApiController : ApiControllerBase
{
    public ApiController(IStoreManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork) : base(blockchainManager, networkManager, meshNetwork)
    {
    }
}
