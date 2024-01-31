using Kryolite.Shared.Blockchain;
using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class BlockResponse : IPacket
{
    public Block? Block { get; set; }

    [MemoryPackConstructor]
    public BlockResponse(Block? block)
    {
        Block = block;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
