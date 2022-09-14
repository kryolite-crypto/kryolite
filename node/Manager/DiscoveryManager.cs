using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class DiscoveryManager : IDiscoveryManager
{
    private List<Host> Hosts = new List<Host>();
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<IDiscoveryManager> logger;

    public DiscoveryManager(ILogger<IDiscoveryManager> logger) => this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void AddHost(Host host)
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
            .Select(x => new DateTimeOffset(x.SystemTime + (DateTime.Now - x.LastSeen)).ToUnixTimeSeconds())
            .ToList();

        timestamps.Add(DateTimeOffset.Now.ToUnixTimeSeconds());

        return DateTimeOffset.FromUnixTimeSeconds((long)timestamps.Average());
    }

    public class Host
    {
        public DateTime LastSeen { get; set; }
        public DateTime SystemTime { get; set; }
    }
}