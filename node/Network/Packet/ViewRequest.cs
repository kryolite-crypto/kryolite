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
    public SHA256Hash ViewHash { get; }

    public ViewRequestByHash(SHA256Hash viewHash)
    {
        ViewHash = viewHash;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoRequest>>();

        logger.LogDebug($"Received ViewRequestByHash from {peer.Uri.ToHostname()}");

        var view = blockchainManager.GetView(ViewHash);
        await peer.ReplyAsync(args.Message.Id, new ViewResponse(view));
    }
}

[MessagePackObject]
public class ViewRequestById : IPacket
{
    [Key(0)]
    public long Id { get; }

    public ViewRequestById(long id)
    {
        Id = id;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoRequest>>();

        logger.LogDebug($"Received ViewRequestById from {peer.Uri.ToHostname()}");

        var view = blockchainManager.GetView(Id);
        await peer.ReplyAsync(args.Message.Id, new ViewResponse(view));
    }
}
