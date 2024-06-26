using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;
using Kryolite.Type;

namespace Kryolite.Grpc.NodeService;

public interface INodeService : IWebsocketService
{
    PublicKey GetPublicKey();

    NodeListResponse GetPeers();

    View? GetViewForId(long id);

    View? GetViewForHash(SHA256Hash hash);

    Block? GetBlock(SHA256Hash hash);

    Vote? GetVote(SHA256Hash hash);

    TransactionDto? GetTransaction(SHA256Hash hash);
    
    void SuggestView(SyncRequest request);

    long FindCommonHeight(HashList hashlist);

    ViewListResponse GetViewsForRange(ViewListRequest request);

    SyncResponse ShouldSync(SyncRequest request);
    Task Broadcast(byte[] data);
}
