using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;

namespace Kryolite.Grpc.NodeService;

public interface IClientFactory : IWebsocketService
{
    T CreateClient<T>(WebsocketChannel channel);
}
