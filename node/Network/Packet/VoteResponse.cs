using Kryolite.Shared.Blockchain;
using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class VoteResponse : IPacket
{
    public Vote? Vote { get; set; }

    public VoteResponse(Vote? vote)
    {
        Vote = vote;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
