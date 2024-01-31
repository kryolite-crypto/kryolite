using MemoryPack;

namespace Kryolite.Node;

// Request & Responses
[MemoryPackable]
[MemoryPackUnion(0, typeof(NodeInfoRequest))]
[MemoryPackUnion(1, typeof(NodeInfoResponse))]
[MemoryPackUnion(4, typeof(HeightRequest))]
[MemoryPackUnion(5, typeof(HeightResponse))]
[MemoryPackUnion(6, typeof(BlockRequest))]
[MemoryPackUnion(7, typeof(BlockResponse))]
[MemoryPackUnion(8, typeof(VoteRequest))]
[MemoryPackUnion(9, typeof(VoteResponse))]
[MemoryPackUnion(10, typeof(ViewRequestByHash))]
[MemoryPackUnion(11, typeof(ViewRequestById))]
[MemoryPackUnion(12, typeof(ViewResponse))]
[MemoryPackUnion(13, typeof(TransactionRequest))]
[MemoryPackUnion(14, typeof(TransactionResponse))]
[MemoryPackUnion(15, typeof(PendingRequest))]
[MemoryPackUnion(16, typeof(PendingResponse))]
[MemoryPackUnion(17, typeof(ViewRequestByRange))]
[MemoryPackUnion(18, typeof(ViewRangeResponse))]
// Broadcasts
[MemoryPackUnion(100, typeof(NodeBroadcast))]
[MemoryPackUnion(101, typeof(ViewBroadcast))]
[MemoryPackUnion(102, typeof(BlockBroadcast))]
[MemoryPackUnion(103, typeof(VoteBroadcast))]
[MemoryPackUnion(104, typeof(TransactionBroadcast))]
public partial interface IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider);
}
