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

    private List<Node> _nodes = new();
    private PublicKey _serverKey;

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
            node = new Node(key, uri, version);
            _nodes.Add(node);

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
