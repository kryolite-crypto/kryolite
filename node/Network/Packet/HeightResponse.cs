using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class HeightResponse : IPacket
{
    [Key(0)]
    public long CommonHeight { get; set; }

    public HeightResponse(long commonHeight)
    {
        CommonHeight = commonHeight;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {

    }
}
