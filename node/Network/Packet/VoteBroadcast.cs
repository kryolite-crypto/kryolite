using System.Collections.Concurrent;
using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class VoteBroadcast : IPacket
{
    [Key(0)]
    public SHA256Hash Votehash { get; set; }

    public VoteBroadcast(SHA256Hash votehash)
    {
        Votehash = votehash;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (peer.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring VoteBroadcast, sync in progress");
            return;
        }

        // Lock on the hashvalue to prevent nodes concurrently downloading votes for same hash
        using var _ = Votehash.Lock();
        
        logger.LogDebug("Received VoteBroadcast from {hostname}", peer.Uri.ToHostname());

        if (storeManager.VoteExists(Votehash))
        {
            return;
        }

        var result = await peer.PostAsync(new VoteRequest(Votehash));

        if (result is null || result.Payload is not VoteResponse response || response.Vote is null)
        {
            return;
        }

        storeManager.AddVote(response.Vote, true);
    }

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
}
