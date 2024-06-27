using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Transport.Websocket;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public sealed class CalleeNodeService(WebsocketChannel channel, IServiceProvider serviceProvider) : NodeService(channel, serviceProvider)
{
    public new ArraySegment<byte> GetPeers()
    {
        var peers = base.GetPeers();
        return Serializer.Serialize(peers);
    }

    public new ArraySegment<byte> GetPublicKey()
    {
        var pubKey = base.GetPublicKey();
        return Serializer.Serialize(pubKey);
    }

    public ArraySegment<byte> GetViewForId(ArraySegment<byte> payload)
    {
        var id = BitConverter.ToInt64(payload);
        var view = base.GetViewForId(id);
        return Serializer.SerializeN(view);
    }

    public ArraySegment<byte> GetViewForHash(ArraySegment<byte> payload)
    {
        var hash = Serializer.Deserialize<SHA256Hash>(payload);
        var view = base.GetViewForHash(hash);
        return Serializer.SerializeN(view);
    }

    public ArraySegment<byte> GetBlock(ArraySegment<byte> payload)
    {
        var hash = Serializer.Deserialize<SHA256Hash>(payload);
        var block = base.GetBlock(hash);
        return Serializer.SerializeN(block);
    }

    public ArraySegment<byte> GetVote(ArraySegment<byte> payload)
    {
        var hash = Serializer.Deserialize<SHA256Hash>(payload);
        var vote = base.GetVote(hash);
        return Serializer.SerializeN(vote);
    }

    public ArraySegment<byte> GetTransaction(ArraySegment<byte> payload)
    {
        var hash = Serializer.Deserialize<SHA256Hash>(payload);
        var tx = base.GetTransaction(hash);
        return Serializer.SerializeN(tx);
    }

    public ArraySegment<byte> SuggestView(ArraySegment<byte> payload)
    {
        var request = Serializer.Deserialize<SyncRequest>(payload);
        base.SuggestView(request);
        return ArraySegment<byte>.Empty;
    }

    public ArraySegment<byte> FindCommonHeight(ArraySegment<byte> payload)
    {
        var hashes = Serializer.Deserialize<HashList>(payload);
        var height = base.FindCommonHeight(hashes);
        return BitConverter.GetBytes(height);
    }

    public ArraySegment<byte> GetViewsForRange(ArraySegment<byte> payload)
    {
        var request = Serializer.Deserialize<ViewListRequest>(payload);
        var response = base.GetViewsForRange(request);
        return Serializer.Serialize(response);
    }

    public ArraySegment<byte> ShouldSync(ArraySegment<byte> payload)
    {
        var request = Serializer.Deserialize<SyncRequest>(payload);
        var response = base.ShouldSync(request);
        return Serializer.Serialize(response);
    }

    public override ArraySegment<byte> CallMethod(byte methodByte, ArraySegment<byte> payload)
    {
        try
        {
            var method = (NodeServiceMethod)methodByte;

            switch (method)
            {
                case NodeServiceMethod.GET_PEERS:
                    return GetPeers();
                case NodeServiceMethod.GET_PUBLIC_KEY:
                    return GetPublicKey();
                case NodeServiceMethod.GET_VIEW_FOR_ID:
                    return GetViewForId(payload);
                case NodeServiceMethod.GET_VIEW_FOR_HASH:
                    return GetViewForHash(payload);
                case NodeServiceMethod.GET_BLOCK:
                    return GetBlock(payload);
                case NodeServiceMethod.GET_VOTE:
                    return GetVote(payload);
                case NodeServiceMethod.GET_TRANSACTION:
                    return GetTransaction(payload);
                case NodeServiceMethod.SUGGEST_VIEW:
                    return SuggestView(payload);
                case NodeServiceMethod.FIND_COMMON_HEIGHT:
                    return FindCommonHeight(payload);
                case NodeServiceMethod.GET_VIEWS_FOR_RANGE:
                    return GetViewsForRange(payload);
                case NodeServiceMethod.SHOULD_SYNC:
                    return ShouldSync(payload);
                default:
                    throw new NotImplementedException(methodByte.ToString());
            }
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<CalleeNodeService>>();
            logger.LogError(ex, "");
            throw new Exception("", ex);
        }
    }
}
