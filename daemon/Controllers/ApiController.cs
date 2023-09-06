using Kryolite.Node;
using Kryolite.Node.Services;
using Kryolite.Shared.Dto;
using System;

namespace Kryolite.Daemon;

public class ApiController : ApiControllerBase
{
    public ApiController(IStoreManager blockchainManager, INetworkManager networkManager, IMeshNetwork meshNetwork, IBufferService<TransactionDto, IncomingTransactionService> txBuffer) : base(blockchainManager, networkManager, meshNetwork, txBuffer)
    {
    }
}
