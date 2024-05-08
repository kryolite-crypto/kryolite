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
                _timer.Period = TimeSpan.FromSeconds(60);
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
                await AdjustConnectionsToClosestNodes(stoppingToken);
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
    /// Get closest nodes and connect to them, disconnecting from outside nodes
    /// </summary>
    private async Task AdjustConnectionsToClosestNodes(CancellationToken stoppingToken)
    {
        var closestNodes = _nodeTable.GetClosestNodes(_nodeKey);

        // Connect to new nodes
        foreach (var node in closestNodes)
        {
            try
            {
                if (_connectedNodes.ContainsKey(node.PublicKey))
                {
                    continue;
                }

                await ConnectTo(node, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                _logger.LogDebug(string.Empty, ex);
            }
        }

        // Remove connections to nodes not in closestNodes anymore
        foreach (var connection in _connectedNodes.Values)
        {
            try
            {
                if (!closestNodes.Any(x => x.PublicKey == connection.Node.PublicKey))
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
    private async Task ConnectTo(Node node, CancellationToken stoppingToken)
    {
        if (_connectedNodes.ContainsKey(node.PublicKey))
        {
            return;
        }

        var channel = WebsocketChannel.ForAddress(node.Uri, stoppingToken);
        var (identity, error) = await channel.GetPublicKey();

        if (identity is null)
        {
            _logger.LogInformation("{hostname}: {error}", node.Uri.ToHostname(), error);
            node.Status = NodeStatus.DEAD;
            return;
        }

        if (!identity.Verify())
        {
            _logger.LogInformation("Signature verification for {hostname} failed", node.Uri.ToHostname());
            return;
        }

        if (identity.PublicKey != node.PublicKey)
        {
            node.PublicKey = identity.PublicKey;
        }

        _nodeTable.MarkNodeAlive(node.PublicKey);

        if (_connectedNodes.ContainsKey(node.PublicKey))
        {
            return;
        }

        _logger.LogInformation("Connecting to {hostname}", node.Uri.ToHostname());

        var authrequest = CreateAuthRequest();

        if (!channel.Connect(authrequest, out var error2))
        {
            _logger.LogInformation("Connecting to {hostname} failed: {error}", node.Uri.ToHostname(), error2);
            return;
        }

        var connection = new NodeConnection(channel, node);

        // Let the connection task run in background
        _ = ConnectionTask(connection);
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
        return _clientFactory.CreateClient(connection.Channel);
    }

    public Task StartListening(Uri uri, PublicKey publicKey, WebsocketChannel channel)
    {
        if (_connectedNodes.ContainsKey(publicKey))
        {
            return Task.CompletedTask;
        }

        var node = new Node(publicKey, uri);
        var connection = new NodeConnection(channel, node);

        _connectedNodes.TryAdd(publicKey, connection);
        _nodeTable.AddNode(publicKey, uri);

        connection.Node = _nodeTable.GetNode(publicKey)!;

        return ConnectionTask(connection);
    }

    /// <summary>
    /// Task to hold live connection, will be closed after disconnecting
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    private async Task ConnectionTask(NodeConnection connection)
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
            node.Status = NodeStatus.DEAD;
            connection.Channel.Dispose();
            _connectedNodes.TryRemove(node.PublicKey, out _);
            NodeDisconnected?.Invoke(this, connection);
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
    public AuthorizationException(string message) : base (message)
    {

    }
}
