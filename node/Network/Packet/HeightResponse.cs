using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class HeightResponse : IPacket
{
    public long CommonHeight { get; set; }

    [MemoryPackConstructor]
    public HeightResponse(long commonHeight)
    {
        CommonHeight = commonHeight;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {

    }
}
