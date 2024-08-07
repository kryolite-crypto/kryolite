using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Interface;
using Kryolite.Shared;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class VoteBroadcast : IBroadcast
{
    public SHA256Hash Votehash;

    public VoteBroadcast()
    {
        Votehash = new();
    }

    public VoteBroadcast(SHA256Hash votehash)
    {
        Votehash = votehash;
    }

    public Task Handle(NodeConnection connection, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (connection.Node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring VoteBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        logger.LogDebug("Received VoteBroadcast from {hostname}", connection.Node.Uri.ToHostname());

        if (storeManager.VoteExists(Votehash))
        {
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient(connection);
        var vote = client.GetVote(Votehash);

        if (vote is not null)
        {
            storeManager.AddVote(vote, true);
        }

        return Task.CompletedTask;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VOTE_BROADCAST;
    }

    public int GetLength()
    {
        return SHA256Hash.HASH_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Votehash);
    }

    public void Deserialize(ref Serializer serializer)
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        serializer.Read(ref buffer, SHA256Hash.HASH_SZ);
        Votehash = new SHA256Hash(buffer);
    }
}
