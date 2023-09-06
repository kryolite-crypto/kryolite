using Kryolite.Node;
using Kryolite.Node.Services;
using Kryolite.Shared.Dto;

namespace Kryolite.Wallet;

public class ApiController : ApiControllerBase
{
    public ApiController(IStoreManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork, IBufferService<TransactionDto, IncomingTransactionService> txBuffer) : base(blockchainManager, networkManager, meshNetwork, txBuffer)
    {
    }
}
