using System.Buffers;
using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;

namespace Kryolite.Node.Network;

public sealed class CallerNodeService(WebsocketChannel channel, IServiceProvider serviceProvider) : NodeService(channel, serviceProvider)
{
    public override NodeListResponse GetPeers()
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_PEERS;
        writer.Advance(1);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.Deserialize<NodeListResponse>(task.Result);
    }

    public override PublicKey GetPublicKey()
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_PUBLIC_KEY;
        writer.Advance(1);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.Deserialize<PublicKey>(task.Result);
    }

    public override View? GetViewForId(long id)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_VIEW_FOR_ID;
        writer.Advance(1);

        Serializer.Serialize(id, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.DeserializeN<View>(task.Result);
    }

    public override View? GetViewForHash(SHA256Hash hash)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_VIEW_FOR_HASH;
        writer.Advance(1);

        Serializer.Serialize(hash, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.DeserializeN<View>(task.Result); 
    }

    public override Block? GetBlock(SHA256Hash hash)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_BLOCK;
        writer.Advance(1);

        Serializer.Serialize(hash, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.DeserializeN<Block>(task.Result); 
    }

    public override Vote? GetVote(SHA256Hash hash)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_VOTE;
        writer.Advance(1);

        Serializer.Serialize(hash, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.DeserializeN<Vote>(task.Result); 
    }

    public override TransactionDto? GetTransaction(SHA256Hash hash)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_VOTE;
        writer.Advance(1);

        Serializer.Serialize(hash, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.DeserializeN<TransactionDto>(task.Result); 
    }

    public override void SuggestView(SyncRequest request)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.SUGGEST_VIEW;
        writer.Advance(1);

        Serializer.Serialize(request, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);
    }

    public override long FindCommonHeight(HashList hashlist)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.FIND_COMMON_HEIGHT;
        writer.Advance(1);

        Serializer.Serialize(hashlist, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return BitConverter.ToInt64(task.Result);
    }

    public override ViewListResponse GetViewsForRange(ViewListRequest request)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.GET_VIEWS_FOR_RANGE;
        writer.Advance(1);

        Serializer.Serialize(request, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.Deserialize<ViewListResponse>(task.Result);
    }

    public override SyncResponse ShouldSync(SyncRequest request)
    {
        var writer = new ArrayBufferWriter<byte>(1);
        writer.GetSpan()[0] = (byte)NodeServiceMethod.SHOULD_SYNC;
        writer.Advance(1);

        Serializer.Serialize(request, writer);

        var task = _channel.SendUnary(writer, CancellationToken.None);
        task.Wait(CancellationToken.None);

        return Serializer.Deserialize<SyncResponse>(task.Result);
    }
}
