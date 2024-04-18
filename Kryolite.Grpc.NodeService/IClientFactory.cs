using Kryolite.Transport.Websocket;

namespace Kryolite.Grpc.NodeService;

public interface IClientFactory
{
    INodeService CreateClient(WebsocketChannel channel);
}
