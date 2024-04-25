using Kryolite.ByteSerializer;
using Kryolite.Shared;

namespace Kryolite.Node.Network;

public partial interface IBroadcast : ISerializable
{
    public Task Handle(NodeConnection connection, IServiceProvider provider);
}
