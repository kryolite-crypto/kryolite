using System.Net;
using System.Numerics;
using Grpc.Core;
using Grpc.Net.Client;
using Kryolite.Node.Repository;
using Kryolite.Shared;

namespace Kryolite.Node.Network;

public class NodeTable
{
    public event EventHandler<Node>? NodeAdded;
    public event EventHandler<Node>? NodeAlive;
    public event EventHandler<Node>? NodeRemoved;

    private static ReaderWriterLockSlim _rwlock = new(LockRecursionPolicy.SupportsRecursion);

    private List<Node> _nodes = new();
    private PublicKey _serverKey;

    public NodeTable(IKeyRepository keyRepo)
    {
        _serverKey = keyRepo.GetKey().PublicKey;
    }

    public int GetNodesCount()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return GetAllNodes().Count();
    }

    public int GetActiveNodesCount()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return GetActiveNodes().Count();
    }

    public List<Node> GetAllNodes()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _nodes.ToList();
    }

    public List<Node> GetActiveNodes()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _nodes.Where(x => x.Status == NodeStatus.ALIVE).ToList();
    }

    public List<Node> GetClosestNodes(PublicKey publicKey)
    {
        using var _ = _rwlock.EnterReadLockEx();
        
        var value = new BigInteger(publicKey);
        return _nodes
            .Where(x => x.Status == NodeStatus.ALIVE)
            .OrderBy(x => BigInteger.Max(new BigInteger(x.PublicKey), value) - BigInteger.Min(new BigInteger(x.PublicKey), value))
            .Take(Constant.MAX_PEERS)
            .ToList();
    }

    public List<Node> GetInactiveNodes()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _nodes.Where(x => x.LastSeen < DateTime.UtcNow.AddMinutes(-15)).ToList();
    }

    public List<Node> GetExpiringNodes()
    {
        using var _ = _rwlock.EnterReadLockEx();
        return _nodes.Where(x => x.LastSeen < DateTime.UtcNow.AddHours(-24)).ToList();
    }

    public void AddNode(PublicKey key, Uri uri, GrpcChannel? channel = null)
    {
        if (key == _serverKey)
        {
            return;
        }

        using var _ = _rwlock.EnterWriteLockEx();

        var node = GetNodeByKey(key);

        if (node is null)
        {
            node = channel is not null ? 
                new Node(key, uri, channel) :
                new Node(key, uri);

            _nodes.Add(node);

            NodeAdded?.Invoke(this, node);
        }

        // Update existing node details
        node.UpdateUri(node.Uri);
        node.LastSeen = DateTime.Now;
    }

    public void MarkNodeDead(PublicKey key)
    {
        using var _ = _rwlock.EnterWriteLockEx();
        var node = GetNodeByKey(key);
        
        if (node is not null)
        {
            node.Status = NodeStatus.DEAD;
        }
    }

    public void MarkNodeAlive(PublicKey key)
    {
        using var _ = _rwlock.EnterWriteLockEx();
        var node = GetNodeByKey(key);
        
        if (node is not null)
        {
            var broadcast = node.Status != NodeStatus.ALIVE;

            node.Status = NodeStatus.ALIVE;
            node.LastSeen = DateTime.UtcNow;

            if (broadcast)
            {
                NodeAlive?.Invoke(this, node);
            }
        }
    }

    public void RemoveNode(Node node)
    {
        using var _ = _rwlock.EnterWriteLockEx();

        if (node.Status != NodeStatus.ALIVE)
        {
            _nodes.Remove(node);
            NodeRemoved?.Invoke(this, node);
        }
    }

    public Node? GetNode(PublicKey key)
    {
        using var _ = _rwlock.EnterReadLockEx();
        return GetNodeByKey(key);
    }

    private Node? GetNodeByKey(PublicKey key)
    {
        return _nodes.Where(x => x.PublicKey == key)
            .FirstOrDefault();
    }
}

public class Node : IDisposable
{
    public PublicKey PublicKey { get; set; }
    public DateTime LastSeen { get; set; }
    public NodeStatus Status { get; set; }
    public GrpcChannel Channel { get; private set; }
    public Uri Uri { get; private set; }
    
    public int FailedConnections = 0;
    public bool IsSyncInProgress { get; set; }
    public bool IsForked { get; set; }

    public Node(PublicKey publicKey, Uri uri)
    {
        PublicKey = publicKey;
        Uri = uri;

        Channel = GrpcChannel.ForAddress(uri);
    }

    public Node(PublicKey publicKey, Uri uri, GrpcChannel channel)
    {
        PublicKey = publicKey;
        Uri = uri;
        Channel = channel;
    }

    public void UpdateUri(Uri uri)
    {
        if (uri == Uri)
        {
            return;
        }

        Uri = uri;

        Channel = GrpcChannel.ForAddress(uri);
    }

    public void Dispose()
    {
        Channel.Dispose();
    }
}

public enum NodeStatus
{
    UKNOWN,
    ALIVE,
    DEAD
}
