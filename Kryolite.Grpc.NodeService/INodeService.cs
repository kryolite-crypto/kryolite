using System.Numerics;
using System.ServiceModel;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Grpc.NodeService;

[ServiceContract]
public interface INodeService
{
    [OperationContract]
    PublicKey GetPublicKey();

    [OperationContract]
    NodeListResponse GetPeers();

    [OperationContract]
    View? GetViewForId(long id);

    [OperationContract]
    View? GetViewForHash(SHA256Hash hash);

    [OperationContract]
    Block? GetBlock(SHA256Hash hash);

    [OperationContract]
    Vote? GetVote(SHA256Hash hash);

    [OperationContract]
    TransactionDto? GetTransaction(SHA256Hash hash);
    
    [OperationContract]
    void SuggestView(SyncRequest request);

    [OperationContract]
    long FindCommonHeight(HashList hashlist);

    [OperationContract]
    ViewListResponse GetViewsForRange(ViewListRequest request);

    [OperationContract]
    SyncResponse ShouldSync(SyncRequest request);

    [OperationContract]
    void Broadcast(BatchForward batch);

    [OperationContract]
    long GenerateChallenge(long nonce);

    [OperationContract]
    IAsyncEnumerable<BatchBroadcast> Listen(AuthRequest request, CancellationToken token);
}
