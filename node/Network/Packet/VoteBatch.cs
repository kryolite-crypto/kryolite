using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class VoteBatch : IPacket
{
    [Key(0)]
    public IList<Vote> Votes { get; set; } = new List<Vote>();

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IBlockchainManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBatch>>();

        logger.LogDebug($"Received {Votes.Count} votes from {peer.Uri.ToHostname()}");

        blockchainManager.AddVoteBatch(Votes);
    }
}
