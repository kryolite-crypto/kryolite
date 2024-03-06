using Kryolite.Shared;

namespace Kryolite.Node.Network;

public partial interface IBroadcast : ISerializable
{
    public Task Handle(Node node, IServiceProvider provider);
}
