using MemoryPack;

namespace Kryolite.Node.Network;

[MemoryPackable]
[MemoryPackUnion(0, typeof(NodeBroadcast))]
[MemoryPackUnion(1, typeof(ViewBroadcast))]
[MemoryPackUnion(2, typeof(BlockBroadcast))]
[MemoryPackUnion(3, typeof(VoteBroadcast))]
[MemoryPackUnion(4, typeof(TransactionBroadcast))]
public partial interface IBroadcast
{
    public Task Handle(Node node, IServiceProvider provider);
}
