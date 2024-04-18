using Kryolite.Grpc.NodeService;
using Kryolite.Transport.Websocket;

namespace Kryolite.Node.Network;

public class ClientFactory(IServiceProvider _serviceProvider) : IClientFactory
{
    public INodeService CreateClient(WebsocketChannel channel)
    {
        return new CallerNodeService(channel, _serviceProvider);
    }
}
