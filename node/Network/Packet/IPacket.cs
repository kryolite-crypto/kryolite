using MessagePack;

namespace Kryolite.Node;

// Request & Responses
[Union(0, typeof(NodeInfoRequest))]
[Union(1, typeof(NodeInfoResponse))]
[Union(4, typeof(HeightRequest))]
[Union(5, typeof(HeightResponse))]
[Union(6, typeof(BlockRequest))]
[Union(7, typeof(BlockResponse))]
[Union(8, typeof(VoteRequest))]
[Union(9, typeof(VoteResponse))]
[Union(10, typeof(ViewRequestByHash))]
[Union(11, typeof(ViewRequestById))]
[Union(12, typeof(ViewResponse))]
[Union(13, typeof(TransactionRequest))]
[Union(14, typeof(TransactionResponse))]
// Broadcasts
[Union(100, typeof(NodeBroadcast))]
[Union(101, typeof(ViewBroadcast))]
[Union(102, typeof(BlockBroadcast))]
[Union(103, typeof(VoteBroadcast))]
[Union(104, typeof(TransactionBroadcast))]
public interface IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider);
}
