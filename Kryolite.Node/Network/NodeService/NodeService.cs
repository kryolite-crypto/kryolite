using Kryolite.Grpc.NodeService;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class NodeService : INodeService, IWebsocketService<NodeService>
{
    private readonly NodeTable _nodeTable;
    private readonly IServiceProvider _sp;
    private readonly ILogger<NodeService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly PublicKey _nodeKey;

    protected WebsocketChannel _channel;

    public NodeService(WebsocketChannel channel, IServiceProvider serviceProvider)
    {
        _channel = channel;
        _sp = serviceProvider;
        _nodeTable = serviceProvider.GetRequiredService<NodeTable>();
        _logger = serviceProvider.GetRequiredService<ILogger<NodeService>>();

        var keyRepository = serviceProvider.GetRequiredService<IKeyRepository>();
        _nodeKey = keyRepository.GetPublicKey();
    }

    public virtual NodeListResponse GetPeers()
    {
        return new NodeListResponse(_nodeTable
            .GetActiveNodes()
            .Select(x => new NodeDto(x.PublicKey, x.Uri
            .ToString(), x.LastSeen))
            .ToList()
        );
    }

    public virtual PublicKey GetPublicKey()
    {
        return _nodeKey;
    }

    public virtual View? GetViewForId(long id)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetView(id);
    }

    public virtual View? GetViewForHash(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetView(hash);
    }

    public virtual Block? GetBlock(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetBlock(hash);
    }

    public virtual Vote? GetVote(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetVote(hash);
    }

    public virtual TransactionDto? GetTransaction(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var tx = storeManager.GetTransactionForHash(hash);

        if (tx is null)
        {
            return null;
        }

        return new TransactionDto(tx);
    }

    public virtual void SuggestView(SyncRequest request)
    {
        using var scope = _sp.CreateScope();
        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var connMan = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        
        var chainState = storeManager.GetChainState();

        if (chainState.Weight >= request.Weight)
        {
            return;
        }

        if (storeManager.GetView(request.ViewHash) is not null)
        {
            return;
        }

        // TODO: Get PublicKey from connection, or have the SyncRequest message be signed
        var node = nodeTable.GetNode(request.PublicKey);

        if (node is null)
        {
            return;
        }

        var connection = new NodeConnection(_channel, node);

        SyncManager.AddToQueue(connection);
    }

    public virtual long FindCommonHeight(HashList hashlist)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        
        foreach (var hash in hashlist.Hashes)
        {
            var view = storeManager.GetView(hash);

            if (view is not null)
            {
                return view.Id;
            }
        }

        return 0;
    }

    public virtual ViewListResponse GetViewsForRange(ViewListRequest request)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        
        var views = new List<ViewResponse>(request.BatchSize);

        for (var i = 0; i < request.BatchSize; i++)
        {
            var view = storeManager.GetView(request.StartHeight + i);

            if (view is null)
            {
                break;
            }

            view.ScheduledTransactions.Clear();
            view.Rewards.Clear();

            var response = new ViewResponse
            {
                View = view,
                Blocks = storeManager.GetBlocks(view.Blocks),
                Votes = storeManager.GetVotes(view.Votes),
                Transactions = storeManager.GetTransactions(view.Transactions).Select(x => new TransactionDto(x)).ToList()
            };

            views.Add(response);
        }

        return new ViewListResponse(views);
    }

    public virtual SyncResponse ShouldSync(SyncRequest request)
    {
        using var scope = _sp.CreateScope();
        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var chainState = storeManager.GetChainState();

        if (chainState.Weight > request.Weight)
        {
            // remote is behind, tell them to sync
            return new SyncResponse(true);
        }

        if (chainState.Weight < request.Weight)
        {
            // we are behind, let's sync
            // TODO: Get PublicKey from connection, or have the SyncRequest message be signed
            var node = nodeTable.GetNode(request.PublicKey);

            if (node is null)
            {
                return new SyncResponse(false);
            }

            var connection = new NodeConnection(_channel, node);
            SyncManager.AddToQueue(connection);
        }

        // different views but equal weight
        return new SyncResponse(false);
    }

    public Task Broadcast(byte[] data)
    {
        return _channel.SendDuplex(data, _cts.Token);
    }

    public virtual ArraySegment<byte> CallMethod(byte method, ArraySegment<byte> payload)
    {
        throw new NotImplementedException();
    }

    public static NodeService CreateClient(WebsocketChannel channel, IServiceProvider serviceProvider)
        => new CallerNodeService(channel, serviceProvider);
}
