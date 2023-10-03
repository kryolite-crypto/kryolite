using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class VoteRequest : IPacket
{
    [Key(0)]
    public SHA256Hash Votehash { get; set; }

    public VoteRequest(SHA256Hash votehash)
    {
        Votehash = votehash; 
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<VoteRequest>>();

        logger.LogDebug($"Received VoteRequest from {peer.Uri.ToHostname()}");

        var vote = blockchainManager.GetVote(Votehash);

        _ = peer.ReplyAsync(args.Message.Id, new VoteResponse(vote));
    }
}
