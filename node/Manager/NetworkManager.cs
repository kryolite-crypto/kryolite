using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class NetworkManager : INetworkManager
{
    private List<NodeHost> Hosts = new List<NodeHost>();
    private DateTime _lastDiscovery = DateTime.MinValue;

    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<INetworkManager> logger;

    public NetworkManager(ILogger<INetworkManager> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void AddHost(NodeHost host)
    {
        if (host.Url is null)
        {
            logger.LogWarning($"Failed to add host. Null hostname for {host.ClientId}");
            return;
        }

        using var _ = rwlock.EnterWriteLockEx();
        
        var existing = Hosts.FirstOrDefault(x => x.ClientId == host.ClientId);

        if (existing == null)
        {
            Hosts.Add(host);
            logger.LogInformation($"Added host {host.Url.ToHostname()}");
        }
        else
        {
            existing.Url = host.Url;
            existing.NodeInfo = host.NodeInfo;
            existing.LastSeen = host.LastSeen;
            existing.IsReachable = host.IsReachable;

            logger.LogInformation($"Updated status for host {host.Url}");
        }
    }

    public int GetHostCount()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Hosts.Count;
    }

    public List<NodeHost> GetHosts()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Hosts;
    }

    public void RemoveHost(NodeHost host)
    {
        using var _ = rwlock.EnterWriteLockEx();
        Hosts.Remove(host);
    }

    public class NodeHost
    {
        public Uri Url { get; set; }
        public ulong ClientId { get; set; }
        public NodeInfo? NodeInfo { get; set; }
        public DateTime LastSeen { get; set; } // TODO unixtime
        public bool IsReachable { get; set; }
        
        public NodeHost(Uri url)
        {
            Url = url;
        }
    }
}

[MessagePackObject]
public class NodeCandidate
{
    [Key(0)]
    public Uri Url { get; init; }
    [Key(1)]
    public ulong ClientId { get; init; }

    public NodeCandidate(Uri url, ulong clientId)
    {
        Url = url;
        ClientId = clientId;
    }
}
