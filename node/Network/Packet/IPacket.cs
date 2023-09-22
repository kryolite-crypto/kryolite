using MessagePack;

namespace Kryolite.Node;

// Request & Responses
[Union(0, typeof(NodeInfoRequest))]
[Union(1, typeof(NodeInfoResponse))]
[Union(2, typeof(DownloadRequest))]
[Union(3, typeof(DownloadResponse))]
[Union(4, typeof(TransactionRequest))]
[Union(5, typeof(TransactionResponse))]
[Union(6, typeof(HeightRequest))]
[Union(7, typeof(HeightResponse))]
// Broadcasts
[Union(100, typeof(TransactionBroadcast))]
[Union(101, typeof(NodeBroadcast))]
public interface IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider);
}
