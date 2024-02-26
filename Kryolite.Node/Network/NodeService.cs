using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MemoryPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

[ServiceContract]
public interface INodeService
{
    [OperationContract]
    PublicKey GetPublicKey();

    [OperationContract]
    List<NodeDto> GetPeers();

    [OperationContract]
    View? GetViewForId(long id);

    [OperationContract]
    View? GetViewForHash(SHA256Hash hash);

    [OperationContract]
    Block? GetBlock(SHA256Hash hash);

    [OperationContract]
    Vote? GetVote(SHA256Hash hash);

    [OperationContract]
    TransactionDto? GetTransaction(SHA256Hash hash);
    
    [OperationContract]
    void SuggestView(PublicKey publicKey, SHA256Hash viewhash, BigInteger weight);

    [OperationContract]
    long FindCommonHeight(List<SHA256Hash> hashes);

    [OperationContract]
    List<ViewResponse> GetViewsForRange(long startHeight, int batchSize);

    [OperationContract]
    bool ShouldSync(PublicKey publicKey, SHA256Hash viewHash, BigInteger weight);

    [OperationContract]
    void Broadcast(PublicKey publicKey, byte[][] messages);

    [OperationContract]
    long GenerateChallenge(long nonce);

    [OperationContract]
    IAsyncEnumerable<byte[][]> Listen(AuthRequest request, long challenge, CancellationToken token);
}

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

    public List<NodeDto> GetPeers()
    {
        return _nodeTable.GetActiveNodes().Select(x => new NodeDto(x.PublicKey, x.Uri.ToString(), x.LastSeen)).ToList();
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

    public void SuggestView(PublicKey publicKey, SHA256Hash viewhash, BigInteger weight)
    {
        using var scope = _sp.CreateScope();
        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var connMan = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        
        var chainState = storeManager.GetChainState();

        if (chainState.Weight >= weight)
        {
            return;
        }

        if (storeManager.GetView(viewhash) is not null)
        {
            return;
        }

        var node = nodeTable.GetNode(publicKey);

        if (node is null)
        {
            return;
        }

        SyncManager.AddToQueue(node);
    }

    public long FindCommonHeight(List<SHA256Hash> hashes)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        
        foreach (var hash in hashes)
        {
            var view = storeManager.GetView(hash);

            if (view is not null)
            {
                return view.Id;
            }
        }

        return 0;
    }

    public List<ViewResponse> GetViewsForRange(long startHeight, int batchSize)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        
        var views = new List<ViewResponse>(batchSize);

        for (var i = 0; i < batchSize; i++)
        {
            var view = storeManager.GetView(startHeight + i);

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

        return views;
    }

    public bool ShouldSync(PublicKey publicKey, SHA256Hash viewHash, BigInteger weight)
    {
        using var scope = _sp.CreateScope();
        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var chainState = storeManager.GetChainState();

        if (chainState.ViewHash != viewHash)
        {
            return true;
        }

        if (chainState.Weight > weight)
        {
            // remote is behind, tell them to sync
            return true;
        }

        if (chainState.Weight < weight)
        {
            // we are behind, let's sync
            var node = nodeTable.GetNode(publicKey);

            if (node is not null)
            {
                SyncManager.AddToQueue(node);
            }
        }

        // different views but equal weight
        return false;
    }

    public void Broadcast(PublicKey publicKey, byte[][] messages)
    {
        using var scope = _sp.CreateScope();

        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var node = nodeTable.GetNode(publicKey);

        foreach (var message in messages)
        {
            var packet = MemoryPackSerializer.Deserialize<IBroadcast>(message);

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

    public IAsyncEnumerable<byte[][]> Listen(AuthRequest authRequest, long challenge, CancellationToken cancellationToken)
    {
        if (!Authorize(authRequest, out var uri))
        {
            return Empty<byte[][]>();
        }

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

    private async IAsyncEnumerable<byte[][]> CreateOutboundChannel(long challenge, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var authResponse = CreateAuthResponse(challenge);

        yield return [MemoryPackSerializer.Serialize(authResponse)];

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
                    break;
                }

                data = await channel.Reader.ReadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Client receive stream terminated with exception");
                break;
            }

            if (data is not null)
            {
                yield return data;
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
