using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node;

[MessagePackObject]
public class HeightRequest : IPacket
{
    [Key(0)]
    public List<SHA256Hash> Views { get; set; } = new();

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeBroadcast>>();

        logger.LogDebug($"Received HeightRequest from {peer.Uri.ToHostname()}");

        foreach (var hash in Views)
        {
            logger.LogDebug($"Searching for View with hash {hash}");
            var view = storeManager.GetView(hash);

            if (view is not null)
            {
                logger.LogDebug($"Found common height at {view.Id}");
                await peer.ReplyAsync(args.Message.Id, new HeightResponse(view.Id));
                return;
            }
        }

        logger.LogDebug($"No common height found");
        await peer.ReplyAsync(args.Message.Id, new HeightResponse(0));
    }
}
