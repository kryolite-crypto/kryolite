using Kryolite.Shared;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MemoryPackable]
public partial class VoteRequest : IPacket
{
    public SHA256Hash Votehash { get; set; }

    [MemoryPackConstructor]
    public VoteRequest(SHA256Hash votehash)
    {
        Votehash = votehash; 
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<VoteRequest>>();

        logger.LogDebug("Received VoteRequest from {hostname}", peer.Uri.ToHostname());

        var vote = blockchainManager.GetVote(Votehash);

        _ = peer.ReplyAsync(args.Message.Id, new VoteResponse(vote));
    }
}
