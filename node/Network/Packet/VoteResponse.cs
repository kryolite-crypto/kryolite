using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class VoteResponse : IPacket
{
    [Key(0)]
    public Vote? Vote { get; }

    public VoteResponse(Vote? vote)
    {
        Vote = vote;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
