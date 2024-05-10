using System.Numerics;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Transport.Websocket;

namespace Kryolite.Node.Network;

public class NodeTable
{
    public event EventHandler<Node>? NodeAdded;
    public event EventHandler<Node>? NodeAlive;
    public event EventHandler<Node>? NodeRemoved;

    private static ReaderWriterLockSlim _rwlock = new(LockRecursionPolicy.SupportsRecursion);

    private PublicKey _serverKey;
    private List<Node> _nodes = new();

    private const int BUCKET_MAX_NODES = 4;

    public NodeTable(IKeyRepository keyRepo)
    {
        _serverKey = keyRepo.GetPublicKey();
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

    public List<Node> GetSortedNodes()
    {
        using var _ = _rwlock.EnterWriteLockEx();

        var self = new Node(_serverKey, new Uri("http://localhost/"), string.Empty);

        // Concatenate local server as temporary node to have a spot in the buckets
        var nodes = _nodes
            .Where(x => x.Status == NodeStatus.ALIVE)
            .Concat([self])
            .OrderBy(x => new BigInteger(x.PublicKey))
            .ToList();

        var selfIx = nodes.IndexOf(self);

        // Rearrange nodes so that self is first and nodes before self are last
        return Enumerable.Concat(nodes[selfIx..], nodes[..selfIx]).ToList();
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

    public void AddNode(PublicKey key, Uri uri, string version)
    {
        if (key == _serverKey)
        {
            return;
        }

        using var _ = _rwlock.EnterWriteLockEx();

        var node = GetNodeByKey(key);

        if (node is null)
        {
            _nodes.Add(node = new Node(key, uri, version));
            NodeAdded?.Invoke(this, node);
        }

        node.Uri = uri;
        node.LastSeen = DateTime.Now;
        node.Version = version;
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

        _nodes.Remove(node);
        NodeRemoved?.Invoke(this, node);
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

public class Node
{
    public PublicKey PublicKey { get; set; }
    public DateTime FirstSeen { get; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; }
    public NodeStatus Status { get; set; }
    public Uri Uri { get; set; }
    public string Version { get; set; }
    
    public int FailedConnections = 0;
    public bool IsSyncInProgress { get; set; }
    public bool IsForked { get; set; }

    public Node(PublicKey publicKey, Uri uri, string version)
    {
        PublicKey = publicKey;
        Uri = uri;
        Version = version;
    }
}

public enum NodeStatus
{
    UKNOWN,
    ALIVE,
    DEAD
}
