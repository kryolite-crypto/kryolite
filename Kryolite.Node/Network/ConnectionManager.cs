using System.Collections.Concurrent;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceModel.Grpc.Client;

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
    private readonly IClientFactory _clientFactory;
    private readonly ConcurrentDictionary<PublicKey, NodeConnection> _connectedNodes = new();
    private readonly ILogger<ConnectionManager> _logger;
    private readonly TimeSpan _timeout;
    private readonly IServiceProvider _sp;

    private int _restoringConnectivity = 0;

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
        };

        NodeDisconnected += async (object? sender, NodeConnection connection) =>
        {
            var node = connection.Node;

            _logger.LogInformation("Disconnected from {node}", node.Uri.ToHostname());

            _nodeTable.MarkNodeDead(node.PublicKey);
            _connectedNodes.TryRemove(node.PublicKey, out _);

            if (_connectedNodes.Count == 0)
            {
                await RestoreConnectivity(stoppingToken);
            }
        };

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            while (!stoppingToken.IsCancellationRequested)
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                await DoPeriodicConnectivityTest(stoppingToken);
                await DoExpiredNodeCleanup();
                await AdjustConnectionsToClosestNodes(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ConnMan       [DOWN]");
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
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var expiringNodes = _nodeTable.GetInactiveNodes();

            await Parallel.ForEachAsync(expiringNodes, stoppingToken, async (node, ct) =>
            {
                try
                {
                    var success = await node.Channel.ConnectAsync(stoppingToken).WithTimeout(_timeout, stoppingToken);

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
    private async Task DoExpiredNodeCleanup()
    {
        var expiringNodes = _nodeTable.GetExpiringNodes();

        foreach (var node in expiringNodes)
        {
            _nodeTable.RemoveNode(node);

            await node.Channel.ShutdownAsync();
            node.Dispose();
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
        // TODO: reace condition
        foreach (var connection in _connectedNodes.Values)
        {
            if (!closestNodes.Any(x => x.PublicKey == connection.Node.PublicKey))
            {
                await connection.Node.Channel.ShutdownAsync();
            }
        }
    }

    private async Task RestoreConnectivity(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var restoring = Interlocked.CompareExchange(ref _restoringConnectivity, 0, 1);

        if (restoring == 1)
        {
            return;
        }

        var opts = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };

begin:
        var nodes = _nodeTable.GetAllNodes();

        await Parallel.ForEachAsync(nodes, opts, async (node, token) =>
        {
            try
            {
                if (node.FailedConnections > 0)
                {
                    // Some throttling for nodes with failed connections
                    // [0, 1, 3, 7, 15, 31, 63, 127]
                    // This makes the for loop wait at max 63 seconds throttling other 
                    // connection attempts, not optimal...
                    var delay = Math.Pow(2, Math.Min(node.FailedConnections, 6)) - 1;
                    
                    _logger.LogDebug("Node {hostname} has {count} connection failures, throttling reconnection for {} seconds",
                        node.Uri.ToHostname(),
                        node.FailedConnections,
                        delay
                    );

                    await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
                }

                await ConnectTo(node, stoppingToken);

                if (_connectedNodes.Count > 0)
                {
                    return;
                }
            }
            catch (TaskCanceledException)
            {
                // Do nothing, we are shutting down
            }
            catch (OperationCanceledException)
            {
                // Do nothing, we are shutting down
            }
        });

        if (_connectedNodes.Count == 0)
        {
            goto begin;
        }

        _restoringConnectivity = 0;
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
    /// Task to hold live connection, will be closed after disconnectiong happens and reconnect attempts do not work
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
            var success = await node.Channel.ConnectAsync(cancellationToken).WithTimeout(_timeout, cancellationToken);

            if (!success)
            {
                connection.Node.FailedConnections++;
                wasConnected = false;
                tcs.SetResult();
                return;
            }

            var client = _clientFactory.CreateClient<INodeService>(node.Channel);

            using (var scope = _sp.CreateScope())
            {
                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
                var chainState = storeManager.GetChainState();

                if (client.ShouldSync(_nodeKey, chainState.ViewHash, chainState.Weight))
                {
                    // Let's add this node to our sync queue
                    SyncManager.AddToQueue(node);
                }
            }

            var nonce = Random.Shared.NextInt64();
            var challenge = client.GenerateChallenge(nonce);

            var authRequest = CreateAuthRequest();
            
            var auth = false;

            await foreach (var data in client.Listen(authRequest, challenge, cancellationToken))
            {
                node.LastSeen = DateTime.Now;

                if (!auth)
                {
                    if (data.Length != 1)
                    {
                        throw new AuthorizationException("expected authorization response but got something else");
                    }

                    var authResponse = MemoryPackSerializer.Deserialize<AuthResponse>(data[0]);

                    if (authResponse is null)
                    {
                        throw new AuthorizationException("expected authorization response but deserialization failed");
                    }

                    if (authResponse.PublicKey != node.PublicKey)
                    {
                        throw new AuthorizationException($"expected authorization response to have public key '{node.PublicKey}' but got '{authResponse.PublicKey}'");
                    }

                    if (authResponse.Nonce != nonce)
                    {
                        throw new AuthorizationException($"expected authorization response to have nonce '{nonce}' but got '{authResponse.Nonce}'");
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
                    foreach (var message in data)
                    {
                        var packet = MemoryPackSerializer.Deserialize<IBroadcast>(message);

                        if (packet is not null)
                        {
                            await PacketManager.Handle(node, packet, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("{node} sent malformed broadcast: {message}", node.Uri, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Do nothing, we are shutting down
        }
        catch (AuthorizationException)
        {
            _logger.LogDebug("{node} authentication failed", node.Uri);
            // Something strange happened, remove node
            _nodeTable.RemoveNode(node);
            _connectedNodes.TryRemove(node.PublicKey, out _);
        }
        catch (Exception ex)
        {
            connection.Node.FailedConnections++;
            _logger.LogDebug("Node {node} got disconnected: {message}", connection.Node.Uri.ToHostname(), ex.Message);
        }
        finally
        {
            await connection.Node.Channel.ShutdownAsync();

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

    public T CreateClient<T>(NodeConnection connection) where T : class
    {
        return _clientFactory.CreateClient<T>(connection.Node.Channel);
    }

    public T CreateClient<T>(Node node) where T : class
    {
        return _clientFactory.CreateClient<T>(node.Channel);
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
