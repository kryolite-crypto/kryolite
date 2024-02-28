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
    List<NodeDto> GetPeers();

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
    void SuggestView(PublicKey publicKey, SHA256Hash viewhash, BigInteger weight);

    [OperationContract]
    long FindCommonHeight(List<SHA256Hash> hashes);

    [OperationContract]
    List<ViewResponse> GetViewsForRange(long startHeight, int batchSize);

    [OperationContract]
    bool ShouldSync(PublicKey publicKey, SHA256Hash viewHash, byte[] weight);

    [OperationContract]
    void Broadcast(PublicKey publicKey, byte[][] messages);

    [OperationContract]
    long GenerateChallenge(long nonce);

    [OperationContract]
    IAsyncEnumerable<byte[][]> Listen(AuthRequest request, long challenge, CancellationToken token);
}
