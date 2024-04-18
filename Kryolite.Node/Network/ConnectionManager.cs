using System.Collections.Concurrent;
using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Transport.Websocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

/// <summary>
/// ConnectionManager manages outgoing connection and NodeTable by periodically pingin clients to se if they are alive
/// </summary>
public class ConnectionManager : BackgroundService, IConnectionManager
{
    public event EventHandler<NodeConnection>? NodeConnected;
    public event EventHandler<NodeConnection>? NodeDisconnected;

    private readonly PublicKey _nodeKey;
    private readonly Uri? _publicAddr;
    private readonly int _port;

    private readonly NodeTable _nodeTable;
    private readonly ConcurrentDictionary<PublicKey, NodeConnection> _connectedNodes = new();
    private readonly ILogger<ConnectionManager> _logger;
    private readonly TimeSpan _timeout;
    private readonly IServiceProvider _sp;
    private readonly IClientFactory _clientFactory;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(1));

    public ConnectionManager(NodeTable nodeTable, IClientFactory clientFactory, IServiceProvider sp, IConfiguration config, ILogger<ConnectionManager> logger)
    {
        _nodeTable = nodeTable;
        _clientFactory = clientFactory;
        _logger = logger;
        _sp = sp;

        using var scope = sp.CreateScope();
        var keyRepository = sp.GetRequiredService<IKeyRepository>();

        _nodeKey = keyRepository.GetPublicKey();
        
        _timeout = TimeSpan.FromSeconds(config.GetValue<int>("timeout"));
        _publicAddr = config.GetValue<Uri?>("publicaddr");
        _port = config.GetValue<int>("port");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConnMan       [UP]");

        _nodeTable.NodeAdded += async (object? sender, Node node) =>
        {
            if (_connectedNodes.Count < Constant.MAX_PEERS)
            {
                await ConnectTo(node, stoppingToken);
            }
        };

        _nodeTable.NodeAlive += async (object? sender, Node node) =>
        {
            if (_connectedNodes.Count < Constant.MAX_PEERS)
            {
                await ConnectTo(node, stoppingToken);
            }
        };

        NodeConnected += (object? sender, NodeConnection connection) =>
        {
            var node = connection.Node;

            _logger.LogInformation("Connected to {node}", node.Uri.ToHostname());
            
            _connectedNodes[node.PublicKey] = connection;
            _nodeTable.MarkNodeAlive(node.PublicKey);

            _timer.Period = TimeSpan.FromSeconds(60);
        };

        NodeDisconnected += (object? sender, NodeConnection connection) =>
        {
            var node = connection.Node;

            _logger.LogInformation("Disconnected from {node}", node.Uri.ToHostname());

            _nodeTable.MarkNodeDead(node.PublicKey);
            _connectedNodes.TryRemove(node.PublicKey, out _);

            if (_connectedNodes.Count == 0)
            {
                _timer.Period = TimeSpan.FromSeconds(15);
            }
        };

        try
        {
            while (await _timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_connectedNodes.Count == 0)
                {
                    await RestoreConnectivity(stoppingToken);
                    continue;
                }

                await DoPeriodicConnectivityTest(stoppingToken);
                await DoExpiredNodeCleanup(stoppingToken);
                await AdjustConnectionsToClosestNodes(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ConnMan       [DOWN]");
        }
        finally
        {
            _timer.Dispose();
        }
    }

    /// <summary>
    /// Ping nodes that have been inactive for over 10 minutes and update their status
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    private async Task DoPeriodicConnectivityTest(CancellationToken stoppingToken)
    {
        try
        {
            var expiringNodes = _nodeTable.GetInactiveNodes();

            await Parallel.ForEachAsync(expiringNodes, stoppingToken, async (node, ct) =>
            {
                try
                {
                    var (success, _) = await node.Channel.Ping();

                    if (success)
                    {
                        _nodeTable.MarkNodeAlive(node.PublicKey);
                    }
                    else
                    {
                        _nodeTable.MarkNodeDead(node.PublicKey);
                    }
                }
                catch (OperationCanceledException)
                {
                    _nodeTable.MarkNodeDead(node.PublicKey);
                }
            });
        }
        catch (TaskCanceledException)
        {
            // We're shutting down, do nothing
        }
    }

    /// <summary>
    /// Cleanup nodes that have been inactive for over 24 hours
    /// </summary>
    private async Task DoExpiredNodeCleanup(CancellationToken stoppingToken)
    {
        var expiringNodes = _nodeTable.GetExpiringNodes();

        foreach (var node in expiringNodes)
        {
            _nodeTable.RemoveNode(node);
            await node.Channel.Disconnect(stoppingToken);
        }
    }

    /// <summary>
    /// Get closest nodes and connect to them, disconnecting from outside nodes
    /// </summary>
    private async Task AdjustConnectionsToClosestNodes(CancellationToken stoppingToken)
    {
        var closestNodes = _nodeTable.GetClosestNodes(_nodeKey);

        // Connect to new nodes
        foreach (var node in closestNodes)
        {
            if (_connectedNodes.ContainsKey(node.PublicKey))
            {
                continue;
            }

            await ConnectTo(node, stoppingToken);
        }

        // Remove connections to nodes not in closestNodes anymore
        foreach (var connection in _connectedNodes.Values)
        {
            if (!closestNodes.Any(x => x.PublicKey == connection.Node.PublicKey))
            {
                await connection.Node.Channel.Disconnect(stoppingToken);
            }
        }
    }

    private async Task RestoreConnectivity(CancellationToken stoppingToken)
    {
        var opts = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };

        var nodes = _nodeTable.GetAllNodes();

        await Parallel.ForEachAsync(nodes, opts, async (node, token) =>
        {
            if (_connectedNodes.Count > 0)
            {
                return;
            }

            try
            {
                await ConnectTo(node, stoppingToken);

                if (_connectedNodes.Count > 0)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Do nothing, we are shutting down
            }
        });
    }

    /// <summary>
    /// Connect to node
    /// </summary>
    /// <param name="node"></param>
    private async Task ConnectTo(Node node, CancellationToken stoppingToken)
    {
        if (_connectedNodes.ContainsKey(node.PublicKey))
        {
            return;
        }

        _logger.LogInformation("Connecting to {hostname}", node.Uri.ToHostname());

        var tcs = new TaskCompletionSource();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var connection = new NodeConnection(node, cts);

        _ = ConnectionTask(connection, tcs, cts.Token);

        // Wait for connection attempt to complete
        await tcs.Task.WaitAsync(stoppingToken);
    }

    /// <summary>
    /// Task to hold live connection, will be closed after disconnecting
    /// </summary>
    /// <param name="node"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task ConnectionTask(NodeConnection connection, TaskCompletionSource tcs, CancellationToken cancellationToken)
    {
        var wasConnected = true;
        var node = connection.Node;

        try
        {
            var authRequest = CreateAuthRequest();
            var success = node.Channel.Connect(authRequest, out _);

            if (!success)
            {
                connection.Node.FailedConnections++;
                wasConnected = false;
                tcs.SetResult();
                return;
            }

            var client = _clientFactory.CreateClient(node.Channel);

            using (var scope = _sp.CreateScope())
            {
                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
                var chainState = storeManager.GetChainState();

                var syncRequest = new SyncRequest
                {
                    PublicKey = _nodeKey,
                    ViewHash = chainState.ViewHash,
                    Weight = chainState.Weight
                };

                Console.WriteLine("Start ShouldSync");
                var syncResponse = client.ShouldSync(syncRequest);
                Console.WriteLine("End ShouldSync");

                if (syncResponse.ShouldSync)
                {
                    // Let's add this node to our sync queue
                    SyncManager.AddToQueue(node);
                }
            }

            var auth = false;

            await foreach (var batch in node.Channel.Broadcasts.Reader.ReadAllAsync(cancellationToken))
            {
                node.LastSeen = DateTime.Now;

                if (batch.Count == 0)
                {
                    continue;
                }

                var data = Serializer.Deserialize<BatchBroadcast>(batch);

                if (!auth)
                {
                    if (data.Messages.Length != 1)
                    {
                        throw new AuthorizationException("expected authorization response but got something else");
                    }

                    var authResponse = Serializer.Deserialize<AuthResponse>(data.Messages[0]) ?? throw new AuthorizationException("expected authorization response but deserialization failed");

                    if (authResponse.PublicKey != node.PublicKey)
                    {
                        throw new AuthorizationException($"expected authorization response to have public key '{node.PublicKey}' but got '{authResponse.PublicKey}'");
                    }

                    if (!authResponse.Verify())
                    {
                        throw new AuthorizationException($"authorization response verification failed");
                    }

                    // reset failed connections counter and mark node connected
                    node.FailedConnections = 0;
                    NodeConnected?.Invoke(this, connection);
                    tcs.SetResult();

                    auth = true;
                    continue;
                }
                
                try
                {
                    foreach (var message in data.Messages)
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

                        if (packet is not null)
                        {
                            await PacketManager.Handle(node, packet, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("{node} sent malformed broadcast: {message}", node.Uri, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Do nothing, we are shutting down
            _logger.LogInformation("ocex");
        }
        catch (AuthorizationException)
        {
            _logger.LogInformation("{node} authentication failed", node.Uri);
            // Something strange happened, remove node
            _nodeTable.RemoveNode(node);
            _connectedNodes.TryRemove(node.PublicKey, out _);
        }
        catch (Exception ex)
        {
            connection.Node.FailedConnections++;
            Console.WriteLine(ex);
            _logger.LogInformation("Node {node} got disconnected: {message}", connection.Node.Uri.ToHostname(), ex.Message);
        }
        finally
        {
            await connection.Node.Channel.Disconnect(cancellationToken);

            if (wasConnected)
            {
                NodeDisconnected?.Invoke(this, connection);
            }

            tcs.TrySetResult();
        }
    }

    private AuthRequest CreateAuthRequest()
    {
        using var scope = _sp.CreateScope();

        var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
        var authRequest = new AuthRequest(_nodeKey, _publicAddr, _port);

        authRequest.Sign(keyRepo.GetPrivateKey());

        return authRequest;
    }

    public List<NodeConnection> GetConnectedNodes()
    {
        return _connectedNodes.Values.ToList();
    }

    public INodeService CreateClient(NodeConnection connection)
    {
        return _clientFactory.CreateClient(connection.Node.Channel);
    }

    public INodeService CreateClient(Node node)
    {
        return _clientFactory.CreateClient(node.Channel);
    }
}

public class NodeConnection(Node node, CancellationTokenSource cts)
{
    public Node Node { get; } = node;
    public CancellationTokenSource CancellationTokenSource { get; } = cts;
}

public class AuthorizationException : Exception
{
    public AuthorizationException(string message) : base (message)
    {

    }
}
