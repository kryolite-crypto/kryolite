using Open.Nat;

namespace Kryolite.Module.Upnp;

public interface IDiscoverer
{
    Task<IEnumerable<NatDevice>> DiscoverDevicesAsync(PortMapper portMapper, CancellationTokenSource cancellationTokenSource);
    Task CreatePortMapAsync(NatDevice device, ushort port);
    Task ReleaseAll();
}
