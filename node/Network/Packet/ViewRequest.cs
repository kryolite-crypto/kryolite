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
    [Key(2)]
    public bool IncludeAll { get; set; }

    public ViewRequestByHash(SHA256Hash viewHash)
    {
        ViewHash = viewHash;
        AsyncReply = false;
        IncludeAll = false;
    }

    public ViewRequestByHash(SHA256Hash viewHash, bool asyncReply)
    {
        ViewHash = viewHash;
        AsyncReply = asyncReply;
        IncludeAll = false;
    }

    public ViewRequestByHash(SHA256Hash viewHash, bool asyncReply, bool includeAll)
    {
        ViewHash = viewHash;
        AsyncReply = asyncReply;
        IncludeAll = IncludeAll;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ViewRequestByHash>>();

        logger.LogDebug("Received ViewRequestByHash from {hostname}", peer.Uri.ToHostname());

        var view = blockchainManager.GetView(ViewHash);
        var response = new ViewResponse(view);

        view?.Rewards.Clear();

        if (IncludeAll && view is not null)
        {
            response.Blocks = blockchainManager.GetBlocks(view.Blocks);
            response.Votes = blockchainManager.GetVotes(view.Votes);
            response.Transactions = blockchainManager.GetTransactions(view.Transactions).Select(x => new TransactionDto(x)).ToList();
        }

        if (!AsyncReply)
        {
            await peer.ReplyAsync(args.Message.Id, response);
            return;
        }

        await peer.SendAsync(args.Message.Id, response);
    }
}

[MessagePackObject]
public class ViewRequestById : IPacket
{
    [Key(0)]
    public long Id { get; set; }
    [Key(1)]
    public bool AsyncReply { get; set; }
    [Key(2)]
    public bool IncludeAll { get; set; }

    public ViewRequestById(long id)
    {
        Id = id;
        AsyncReply = false;
        IncludeAll = false;
    }

    public ViewRequestById(long id, bool asyncReply)
    {
        Id = id;
        AsyncReply = asyncReply;
    }

    public ViewRequestById(long id, bool asyncReply, bool includeAll)
    {
        Id = id;
        AsyncReply = asyncReply;
        IncludeAll = includeAll;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ViewRequestById>>();

        logger.LogDebug($"Received ViewRequestById from {peer.Uri.ToHostname()}");

        var view = blockchainManager.GetView(Id);
        var response = new ViewResponse(view);

        view?.Rewards.Clear();

        if (IncludeAll && view is not null)
        {
            response.Blocks = blockchainManager.GetBlocks(view.Blocks);
            response.Votes = blockchainManager.GetVotes(view.Votes);
            response.Transactions = blockchainManager.GetTransactions(view.Transactions).Select(x => new TransactionDto(x)).ToList();
        }

        if (!AsyncReply)
        {
            await peer.ReplyAsync(args.Message.Id, response);
            return;
        }

        await peer.SendAsync(args.Message.Id + 1, response);
    }
}

[MessagePackObject]
public class ViewRequestByRange : IPacket
{
    [Key(0)]
    public long StartId { get; set; }
    [Key(1)]
    public long Count { get; set; }

    public ViewRequestByRange(long startId, long count)
    {
        StartId = startId;
        Count = count;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ViewRequestById>>();

        logger.LogDebug($"Received ViewRequestByRange from {peer.Uri.ToHostname()}");

        var responses = new ViewRangeResponse();

        for (var i = 0; i < Count; i++)
        {
            var view = blockchainManager.GetView(StartId + i);

            if (view is null)
            {
                break;
            }

            view.Rewards.Clear();

            var response = new ViewResponse(view)
            {
                Blocks = blockchainManager.GetBlocks(view.Blocks),
                Votes = blockchainManager.GetVotes(view.Votes),
                Transactions = blockchainManager.GetTransactions(view.Transactions).Select(x => new TransactionDto(x)).ToList()
            };

            responses.Views.Add(response);
        }

        await peer.ReplyAsync(args.Message.Id, responses);
    }
}
