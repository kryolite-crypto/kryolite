using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;

namespace Kryolite.Node;

public class NetworkManager : INetworkManager
{
    private Dictionary<ulong, NodeHost> Hosts = new();

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

        if (!Hosts.TryGetValue(host.ClientId, out var existing))
        {
            Hosts.Add(host.ClientId, host);
            logger.LogInformation($"Added host {host.Url.ToHostname()}");
        }
        else
        {
            existing.Url = host.Url;
            existing.NodeInfo = host.NodeInfo;
            existing.LastSeen = host.LastSeen;
            existing.IsReachable = host.IsReachable;

            logger.LogInformation($"Updated status for host {host.Url.ToHostname()}");
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
        return Hosts.Values.ToList();
    }

    public void RemoveHost(NodeHost host)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (Hosts.ContainsKey(host.ClientId))
        {
            Hosts.Remove(host.ClientId);
        }
    }

    public bool Ban(ulong clientId)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (Hosts.TryGetValue(clientId, out var host))
        {
            host.Ban();

            if (host.IsBanned())
            {
                logger.LogInformation($"Banned {host.Url.ToHostname()} for {Math.Round((host.BannedUntil - DateTimeOffset.Now).TotalMinutes)} minutes");
            }

            return host.IsBanned();
        }

        return false;
    }

    public bool IsBanned(ulong clientId)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (Hosts.TryGetValue(clientId, out var host))
        {
            return host.IsBanned();
        }

        return false;
    }

    public bool IsBanned(string url)
    {
        using var _ = rwlock.EnterReadLockEx();

        var host = Hosts.Values.Where(x => x.Url.ToHostname() == url)
            .FirstOrDefault();

        if (host is null)
        {
            return false;
        }

        return host.IsBanned();
    }

    public class NodeHost
    {
        public Uri Url { get; set; }
        public ulong ClientId { get; set; }
        public NodeInfoResponse? NodeInfo { get; set; }
        public DateTime LastSeen { get; set; } // TODO unixtime
        public bool IsReachable { get; set; }

        public DateTimeOffset BannedUntil { get; set; }
        private int BanCount { get; set; }
        
        public NodeHost(Uri url)
        {
            Url = url;
        }

        public void Ban()
        {
            if (BanCount > 1)
            {
                BannedUntil = DateTimeOffset.Now.AddMinutes((BanCount - 1) * 5);
            }

            BanCount++;
        }

        public bool IsBanned()
        {
            return BannedUntil > DateTimeOffset.Now;
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
