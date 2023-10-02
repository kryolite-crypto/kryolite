using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class BlockResponse : IPacket
{
    [Key(0)]
    public Block? Block { get; }

    public BlockResponse(Block? block)
    {
        Block = block;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
