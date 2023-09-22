using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.ViewEngines;

namespace Kryolite.Node;

[MessagePackObject]
public class HeightRequest : IPacket
{
    [Key(0)]
    public List<SHA256Hash> Views { get; } = new();

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var meshNetwork = scope.ServiceProvider.GetRequiredService<IMeshNetwork>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeBroadcast>>();

        logger.LogDebug($"Received HeightRequest from {peer.Uri.ToHostname()}");

        for (int i = 0; i < Views.Count; i++)
        {
            var view = storeManager.GetView(Views[i]);

            if (view is null)
            {
                continue;
            }

            _ = peer.ReplyAsync(args.Message.Id, new HeightResponse(view.Height ?? 1));
            return;
        }

        _ = peer.ReplyAsync(args.Message.Id, new HeightResponse(1));
    }
}
