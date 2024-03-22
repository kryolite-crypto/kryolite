using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;
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

        var node = nodeTable.GetNode(request.PublicKey);

        if (node is null)
        {
            return;
        }

        SyncManager.AddToQueue(node);
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

        if (chainState.ViewHash != request.ViewHash)
        {
            return new SyncResponse(true);
        }

        if (chainState.Weight > request.Weight)
        {
            // remote is behind, tell them to sync
            return new SyncResponse(true);
        }

        if (chainState.Weight < request.Weight)
        {
            // we are behind, let's sync
            var node = nodeTable.GetNode(request.PublicKey);

            if (node is not null)
            {
                SyncManager.AddToQueue(node);
            }
        }

        // different views but equal weight
        return new SyncResponse(false);
    }

    public virtual void Broadcast(BatchForward batch)
    {
        using var scope = _sp.CreateScope();

        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var node = nodeTable.GetNode(batch.PublicKey);

        foreach (var message in batch.Batch.Messages)
        {
            if (message.Length == 0)
            {
                continue;
            }

            var packetId = (SerializerEnum)message[0];

            IBroadcast? packet = packetId switch
            {
                SerializerEnum.BLOCK_BROADCAST => Serializer.Deserialize<BlockBroadcast>(message),
                SerializerEnum.NODE_BROADCAST => Serializer.Deserialize<NodeBroadcast>(message),
                SerializerEnum.TRANSACTION_BROADCAST => Serializer.Deserialize<TransactionBroadcast>(message),
                SerializerEnum.VIEW_BROADCAST => Serializer.Deserialize<ViewBroadcast>(message),
                SerializerEnum.VOTE_BROADCAST => Serializer.Deserialize<VoteBroadcast>(message),
                _ => null
            };

            if (packet is not null && node is not null)
            {
                PacketManager.Handle(node, packet, CancellationToken.None);
            }
        }
    }

    public virtual IAsyncEnumerable<BatchBroadcast> Listen(AuthRequest authRequest, CancellationToken cancellationToken)
    {
        if (!Authorize(authRequest, out var uri))
        {
            return Empty<BatchBroadcast>();
        }

        var nodeBroadcast = new NodeBroadcast(authRequest, uri.ToString());
        BroadcastManager.Broadcast(nodeBroadcast);

        var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token).Token;

        return CreateOutboundChannel(token);
    }

    private bool Authorize(AuthRequest authRequest, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        if (_nodeKey == authRequest.PublicKey)
        {
            return false;
        }

        if (!authRequest.Verify())
        {
            _logger.LogDebug("AuthRequest verification failed");
            return false;
        }

        if (authRequest.NetworkName != Constant.NETWORK_NAME)
        {
            _logger.LogDebug("Invalid network name");
            return false;
        }

        if (authRequest.ApiLevel < Constant.MIN_API_LEVEL)
        {
            _logger.LogDebug("Too low apilevel");
            return false;
        }

        if (!string.IsNullOrEmpty(authRequest.PublicUri))
        {
            uri = new Uri(authRequest.PublicUri);
            _nodeTable.AddNode(authRequest.PublicKey, uri);
        }
        else
        {
            var builder = new UriBuilder("http", _context.HttpContext!.Connection.RemoteIpAddress!.ToString(), authRequest.Port);
            uri = builder.Uri;
            _nodeTable.AddNode(authRequest.PublicKey, uri);
        }

        return true;
    }

    private AuthResponse CreateAuthResponse()
    {
        using var scope = _sp.CreateScope();
        var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();

        var authResponse = new AuthResponse(_nodeKey, Random.Shared.NextInt64());

        authResponse.Sign(keyRepo.GetPrivateKey());

        return authResponse;
    }

    private async IAsyncEnumerable<BatchBroadcast> CreateOutboundChannel([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var authResponse = CreateAuthResponse();

        yield return new BatchBroadcast([Serializer.Serialize(authResponse)]);

        var channel = Channel.CreateUnbounded<byte[][]>();
        var action = new ActionBlock<byte[][]>(async msg => await channel.Writer.WriteAsync(msg, cancellationToken));

        using var sub = BroadcastManager.Subscribe(action);

        while (!cancellationToken.IsCancellationRequested)
        {
            byte[][]? data = null;

            try
            {
                var open = await channel.Reader.WaitToReadAsync(cancellationToken);

                if (!open)
                {
                    yield break;
                }

                data = await channel.Reader.ReadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Client receive stream terminated with exception");
                yield break;
            }

            if (data is not null)
            {
                yield return new BatchBroadcast(data);
            }
        }
    }

    public virtual ArraySegment<byte> CallMethod(byte method, ArraySegment<byte> payload)
    {
        throw new NotImplementedException();
    }

    public static NodeService CreateClient(WebsocketChannel channel, IServiceProvider serviceProvider)
        => new CallerNodeService(channel, serviceProvider);

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private static async IAsyncEnumerable<T> Empty<T>()
    {
        yield break;
    }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
}
