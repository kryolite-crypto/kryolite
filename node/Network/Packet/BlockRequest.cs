using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class BlockRequest : IPacket
{
    [Key(0)]
    public SHA256Hash Blockhash { get; set; }

    public BlockRequest(SHA256Hash blockhash)
    {
        Blockhash = blockhash;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BlockRequest>>();

        logger.LogDebug($"Received BlockRequest from {peer.Uri.ToHostname()}");

        var block = blockchainManager.GetBlock(Blockhash);
        await peer.ReplyAsync(args.Message.Id, new BlockResponse(block));
    }
}
