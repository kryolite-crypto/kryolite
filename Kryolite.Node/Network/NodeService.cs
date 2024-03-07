using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class NodeService : INodeService
{
    private readonly NodeTable _nodeTable;
    private readonly IServiceProvider _sp;
    private readonly ILogger<NodeService> _logger;
    private readonly IHttpContextAccessor _context;
    private readonly CancellationTokenSource _cts = new();
    private readonly PublicKey _nodeKey;

    private static MemoryCache _challenges = new(new MemoryCacheOptions());

    public NodeService(NodeTable nodeTable, IServiceProvider sp, IHostApplicationLifetime lifetime, ILogger<NodeService> logger, IHttpContextAccessor context)
    {
        _nodeTable = nodeTable;
        _sp = sp;
        _logger = logger;
        _context = context;

        using var scope = sp.CreateScope();
        var keyRepository = sp.GetRequiredService<IKeyRepository>();

        _nodeKey = keyRepository.GetPublicKey();

        lifetime.ApplicationStopping.Register(() => _cts.Cancel());
    }

    public NodeListResponse GetPeers()
    {
        return new NodeListResponse(_nodeTable
            .GetActiveNodes()
            .Select(x => new NodeDto(x.PublicKey, x.Uri
            .ToString(), x.LastSeen))
            .ToList()
        );
    }

    public PublicKey GetPublicKey()
    {
        return _nodeKey;
    }

    public View? GetViewForId(long id)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetView(id);
    }

    public View? GetViewForHash(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetView(hash);
    }

    public Block? GetBlock(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetBlock(hash);
    }

    public Vote? GetVote(SHA256Hash hash)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        return storeManager.GetVote(hash);
    }

    public TransactionDto? GetTransaction(SHA256Hash hash)
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

    public void SuggestView(SyncRequest request)
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

    public long FindCommonHeight(HashList hashlist)
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

    public ViewListResponse GetViewsForRange(ViewListRequest request)
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

    public SyncResponse ShouldSync(SyncRequest request)
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

    public void Broadcast(BatchForward batch)
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

    public long GenerateChallenge(long nonce)
    {
        var challenge = Random.Shared.NextInt64();
        _challenges.Set(challenge, nonce, DateTimeOffset.Now.AddSeconds(30));

        return challenge;
    }

    public IAsyncEnumerable<BatchBroadcast> Listen(AuthRequest authRequest, CancellationToken cancellationToken)
    {
        if (!Authorize(authRequest, out var uri))
        {
            return Empty<BatchBroadcast>();
        }

        var challenge = authRequest.Challenge;
        authRequest.Challenge = 0;

        var nodeBroadcast = new NodeBroadcast(authRequest, uri.ToString());
        BroadcastManager.Broadcast(nodeBroadcast);

        var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token).Token;

        return CreateOutboundChannel(challenge, token);
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

    private AuthResponse CreateAuthResponse(long challenge)
    {
        using var scope = _sp.CreateScope();
        var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();

        var nonce = (long)(_challenges.Get(challenge) ?? 0L);
        var authResponse = new AuthResponse(_nodeKey, nonce);

        authResponse.Sign(keyRepo.GetPrivateKey());

        return authResponse;
    }

    private async IAsyncEnumerable<BatchBroadcast> CreateOutboundChannel(long challenge, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var authResponse = CreateAuthResponse(challenge);

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

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private static async IAsyncEnumerable<T> Empty<T>()
    {
        yield break;
    }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
}
