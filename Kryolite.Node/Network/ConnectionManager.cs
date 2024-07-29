using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Interface;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Transport.Websocket;
using Kryolite.Type;
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
    private readonly IServiceProvider _sp;
    private readonly IClientFactory _clientFactory;

    private readonly ReaderWriterLockSlim _rwlock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(5));

    public ConnectionManager(NodeTable nodeTable, IClientFactory clientFactory, IServiceProvider sp, IConfiguration config, ILogger<ConnectionManager> logger)
    {
        _nodeTable = nodeTable;
        _clientFactory = clientFactory;
        _logger = logger;
        _sp = sp;

        using var scope = sp.CreateScope();
        var keyRepository = sp.GetRequiredService<IKeyRepository>();

        _nodeKey = keyRepository.GetPublicKey();

        _publicAddr = config.GetValue<Uri?>("publicaddr");
        _port = config.GetValue<int>("port");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConnMan       [UP]");

        _nodeTable.NodeAdded += async (object? sender, Node node) =>
        {
            try
            {
                if (_connectedNodes.Count < Constant.MAX_PEERS)
                {
                    await ConnectTo(node, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
        };

        _nodeTable.NodeAlive += async (object? sender, Node node) =>
        {
            try
            {
                if (_connectedNodes.Count < Constant.MAX_PEERS)
                {
                    await ConnectTo(node, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
        };

        NodeConnected += (object? sender, NodeConnection connection) =>
        {
            try
            {
                var node = connection.Node;

                _logger.LogInformation("Connected to {node}", node.Uri.ToHostname());

                _connectedNodes[node.PublicKey] = connection;
                _timer.Period = TimeSpan.FromMinutes(5);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
        };

        NodeDisconnected += (object? sender, NodeConnection connection) =>
        {
            try
            {
                var node = connection.Node;

                _logger.LogInformation("Disconnected from {node}", node.Uri.ToHostname());

                if (_connectedNodes.Count == 0)
                {
                    _timer.Period = TimeSpan.FromSeconds(15);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
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
                await BalanceConnections(stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation("ConnMan       [DOWN]");
            _logger.LogDebug(ex, "");
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "");
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
            _logger.LogInformation("Update inactive node status");

            var expiringNodes = _nodeTable.GetInactiveNodes();

            await Parallel.ForEachAsync(expiringNodes, stoppingToken, async (node, ct) =>
            {
                try
                {
                    using var channel = WebsocketChannel.ForAddress(node.Uri, stoppingToken);
                    var (success, _) = await channel.Ping();

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
                catch (Exception ex)
                {
                    _logger.LogInformation(ex.Message);
                    _logger.LogDebug(string.Empty, ex);
                }
            });
        }
        catch (TaskCanceledException)
        {
            // We're shutting down, do nothing
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex.Message);
            _logger.LogDebug(string.Empty, ex);
        }
    }

    /// <summary>
    /// Cleanup nodes that have been inactive for over 24 hours
    /// </summary>
    private async Task DoExpiredNodeCleanup(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup expired nodes");

        var expiringNodes = _nodeTable.GetExpiringNodes();

        foreach (var node in expiringNodes)
        {
            try
            {
                _nodeTable.RemoveNode(node);

                if (_connectedNodes.TryGetValue(node.PublicKey, out var connection))
                {
                    await connection.Channel.Disconnect(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                _logger.LogDebug(string.Empty, ex);
            }
        }
    }

    /// <summary>
    /// Balance connections
    /// </summary>
    private async Task BalanceConnections(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Balancing connections between nodes");

        var sortedNodes = _nodeTable.GetSortedNodes();

        if (sortedNodes.Count == 0)
        {
            return;
        }

        var validConnections = new HashSet<Node>(8);

        for (var i = 0; i < Constant.MAX_PEERS; i++)
        {
            var target = i * Constant.MAX_PEERS;
            var node = sortedNodes[target % sortedNodes.Count];

            // Add to valid connections list and connect if not already connected
            if (validConnections.Add(node) && !_connectedNodes.ContainsKey(node.PublicKey))
            {
                await ConnectTo(node, stoppingToken);
            }
        }

        // Remove outgoing connections that we did not try to connect in previous foreach loop
        foreach (var connection in _connectedNodes)
        {
            if (_connectedNodes.Count < Constant.MAX_PEERS)
            {
                break;
            }

            if (!connection.Value.Channel.IsOutgoing)
            {
                continue;
            }

            if (!validConnections.Contains(connection.Value.Node))
            {
                await connection.Value.Channel.Disconnect(stoppingToken);
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
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                _logger.LogDebug(string.Empty, ex);
            }
        });
    }

    /// <summary>
    /// Connect to node
    /// </summary>
    /// <param name="node"></param>
    private Task ConnectTo(Node node, CancellationToken stoppingToken)
    {
        using var _lock = _rwlock.EnterWriteLockEx();

        if (_connectedNodes.ContainsKey(node.PublicKey))
        {
            return Task.CompletedTask;
        }

        var channel = WebsocketChannel.ForAddress(node.Uri, stoppingToken);
        var (identity, error) = channel.GetPublicKey().GetAwaiter().GetResult();

        if (identity is null)
        {
            _logger.LogInformation("{hostname}: {error}", node.Uri.ToHostname(), error);
            node.Status = NodeStatus.DEAD;
            return Task.CompletedTask;
        }

        if (!identity.Verify())
        {
            _logger.LogInformation("Signature verification for {hostname} failed", node.Uri.ToHostname());
            return Task.CompletedTask;
        }

        if (identity.PublicKey != node.PublicKey)
        {
            node.PublicKey = identity.PublicKey;
        }

        node.Status = NodeStatus.ALIVE;

        if (_connectedNodes.ContainsKey(node.PublicKey))
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Connecting to {hostname}", node.Uri.ToHostname());

        var authrequest = CreateAuthRequest();

        if (!channel.Connect(authrequest, out var error2))
        {
            _logger.LogInformation("Connecting to {hostname} failed: {error}", node.Uri.ToHostname(), error2);
            return Task.CompletedTask;
        }

        var connection = new NodeConnection(channel, node);
        var tcs = new TaskCompletionSource();

        // Let the connection task run in background
        _ = ConnectionTask(connection, tcs);

        // Return task that will wait for connection handshake and auth to pass
        return tcs.Task;
    }

    private AuthRequest CreateAuthRequest()
    {
        using var scope = _sp.CreateScope();

        var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var authRequest = new AuthRequest(_nodeKey, _publicAddr, _port, version);

        authRequest.Sign(keyRepo.GetPrivateKey());

        return authRequest;
    }

    public List<NodeConnection> GetConnectedNodes()
    {
        return [.. _connectedNodes.Values];
    }

    public INodeService CreateClient(NodeConnection connection)
    {
        return _clientFactory.CreateClient(connection.Channel);
    }

    public Task StartListening(Uri uri, PublicKey publicKey, WebsocketChannel channel, string version)
    {
        using var _ = _rwlock.EnterWriteLockEx();

        if (_connectedNodes.ContainsKey(publicKey))
        {
            return Task.CompletedTask;
        }

        _nodeTable.AddNode(publicKey, uri, version);

        var node = _nodeTable.GetNode(publicKey)!;
        node.Status = NodeStatus.ALIVE;

        var connection = new NodeConnection(channel, node);
        var tcs = new TaskCompletionSource();

        // Let the connection task run in background
        var task = ConnectionTask(connection, tcs);

        // Wait for connection handshake and auth to pass
        tcs.Task.GetAwaiter().GetResult();

        return task;
    }

    /// <summary>
    /// Task to hold live connection, will be closed after disconnecting
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    private async Task ConnectionTask(NodeConnection connection, TaskCompletionSource tcs)
    {
        _connectedNodes.TryAdd(connection.Node.PublicKey, connection);

        var node = connection.Node;

        try
        {
            var client = _clientFactory.CreateClient(connection.Channel);

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

                if (client.ShouldSync(syncRequest).ShouldSync)
                {
                    // Let's add this node to our sync queue
                    SyncManager.AddToQueue(connection);
                }
            }

            NodeConnected?.Invoke(this, connection);
            connection.Channel.ConnectedSince = DateTime.UtcNow;

            tcs.TrySetResult();

            await foreach (var batch in connection.Channel.Broadcasts.Reader.ReadAllAsync(connection.Channel.ConnectionToken))
            {
                node.LastSeen = DateTime.Now;

                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    var data = Serializer.Deserialize<BatchBroadcast>(batch);

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
                            await PacketManager.Handle(connection, packet, connection.Channel.ConnectionToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("{node} sent malformed broadcast: {message}", node.Uri, ex.Message);
                }
            }

            _logger.LogInformation("Node {node} disconnected", connection.Node.Uri.ToHostname());
        }
        catch (OperationCanceledException ocex)
        {
            // Do nothing, we are shutting down
            _logger.LogDebug(ocex, "");
        }
        catch (AuthorizationException)
        {
            _logger.LogInformation("{node} authentication failed", node.Uri);
            // Something strange happened, remove node
            _nodeTable.RemoveNode(node);
        }
        catch (Exception ex)
        {
            connection.Node.FailedConnections++;
            _logger.LogInformation("Node {node} got disconnected: {message}", connection.Node.Uri.ToHostname(), ex.Message);
            _logger.LogDebug(ex, "");
        }
        finally
        {
            connection.Channel.Dispose();
            _connectedNodes.TryRemove(node.PublicKey, out _);
            NodeDisconnected?.Invoke(this, connection);
            tcs.TrySetResult();
        }
    }
}

public class NodeConnection(WebsocketChannel channel, Node node)
{
    public WebsocketChannel Channel { get; set; } = channel;
    public Node Node { get; set; } = node;
}

public class AuthorizationException : Exception
{
    public AuthorizationException(string message) : base(message)
    {

    }
}
