using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

public class NetworkManager : INetworkManager
{
    private List<NodeHost> Hosts = new List<NodeHost>();
    private DateTime _lastDiscovery = DateTime.MinValue;

    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<INetworkManager> logger;
    private BroadcastBlock<PowBlock> BlockProposedBroadcast = new BroadcastBlock<PowBlock>(i => i);

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
        
        var existing = Hosts.FirstOrDefault(x => x.Url == host.Url);

        if (existing == null)
        {
            Hosts.Add(host);
            logger.LogInformation($"Added host {host.Url.ToHostname()}");
        }
        else
        {
            existing.ClientId = host.ClientId;
            existing.NodeInfo = host.NodeInfo;
            existing.LastSeen = host.LastSeen;
            existing.IsReachable = host.IsReachable;

            logger.LogInformation($"Updated status for host {host.Url}");
        }
    }

    public DateTimeOffset GetNetworkTime()
    {
        using var _ = rwlock.EnterReadLockEx();
        var maxAge = DateTime.Now.AddHours(-1);

        var timestamps = Hosts.Where(x => x.LastSeen > maxAge)
            .Where(x => x.NodeInfo is not null)
            .Take(100)
            .OrderBy(arg => Guid.NewGuid())
            .Take(10)
            .Select(x => new DateTimeOffset(x.NodeInfo!.CurrentTime + (DateTime.UtcNow - x.LastSeen)).ToUnixTimeSeconds())
            .ToList();

        timestamps.Add(DateTimeOffset.Now.ToUnixTimeSeconds());

        return DateTimeOffset.FromUnixTimeSeconds((long)timestamps.Average());
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

    public bool ProposeBlock(PowBlock block)
    {
        BlockProposedBroadcast.Post(block);
        return true;
    }

    public IDisposable OnBlockProposed(ITargetBlock<PowBlock> action)
    {
        return BlockProposedBroadcast.LinkTo(action);
    }

    public class NodeHost
    {
        public Uri Url { get; init; }
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
