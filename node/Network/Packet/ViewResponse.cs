using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class ViewResponse : IPacket
{
    [Key(0)]
    public View? View { get; }

    public ViewResponse(View? view)
    {
        View = view;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
