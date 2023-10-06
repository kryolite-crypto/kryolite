using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class ViewRequestByHash : IPacket
{
    [Key(0)]
    public SHA256Hash ViewHash { get; set; }
    [Key(1)]
    public bool AsyncReply { get; set; }

    public ViewRequestByHash(SHA256Hash viewHash)
    {
        ViewHash = viewHash;
        AsyncReply = false;
    }

    public ViewRequestByHash(SHA256Hash viewHash, bool asyncReply)
    {
        ViewHash = viewHash;
        AsyncReply = asyncReply;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ViewRequestByHash>>();

        logger.LogDebug($"Received ViewRequestByHash from {peer.Uri.ToHostname()}");

        var view = blockchainManager.GetView(ViewHash);

        if (!AsyncReply)
        {
            await peer.ReplyAsync(args.Message.Id, new ViewResponse(view));
            return;
        }

        await peer.SendAsync(args.Message.Id, new ViewResponse(view));
    }
}

[MessagePackObject]
public class ViewRequestById : IPacket
{
    [Key(0)]
    public long Id { get; set; }
    [Key(1)]
    public bool AsyncReply { get; set; }

    public ViewRequestById(long id)
    {
        Id = id;
        AsyncReply = false;
    }

    public ViewRequestById(long id, bool asyncReply)
    {
        Id = id;
        AsyncReply = asyncReply;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ViewRequestById>>();

        logger.LogDebug($"Received ViewRequestById from {peer.Uri.ToHostname()}");

        var view = blockchainManager.GetView(Id);

        if (!AsyncReply)
        {
            await peer.ReplyAsync(args.Message.Id, new ViewResponse(view));
            return;
        }

        await peer.SendAsync(args.Message.Id, new ViewResponse(view));
    }
}

public class ResponseType
{

}