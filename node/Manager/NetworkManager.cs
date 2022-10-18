using System.Net;
using System.Numerics;
using Marccacoin.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class NetworkManager : INetworkManager
{
    private List<NodeHost> Hosts = new List<NodeHost>();
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<INetworkManager> logger;

    public NetworkManager(ILogger<INetworkManager> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void AddHost(NodeHost host)
    {
        using var _ = rwlock.EnterWriteLockEx();
        Hosts.Add(host);
    }

    public DateTimeOffset GetNetworkTime()
    {
        using var _ = rwlock.EnterReadLockEx();

        var maxAge = DateTime.Now.AddHours(-1);

        var timestamps = Hosts.Where(x => x.LastSeen > maxAge)
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

    public class NodeHost
    {
        public NodeInfo? NodeInfo { get; init; }
        public DateTime LastSeen { get; init; }
    }
}

[MessagePackObject]
public class NodeInfo
{
    [Key(0)]
    public DateTime CurrentTime { get; init; }
    [Key(1)]
    public long Height { get; init; }
    [Key(2)]
    public BigInteger TotalWork { get; init; }
    [Key(3)]
    public SHA256Hash LastHash { get; init; }
    [Key(4)]
    public List<string> Peers { get; init; } = new List<string>();
}

