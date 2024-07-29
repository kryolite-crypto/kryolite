using Open.Nat;

namespace Kryolite.Module.Upnp;

public class Discoverer : IDiscoverer
{
    private readonly NatDiscoverer _discoverer = new ();
    private readonly List<NatDevice> _devices = new();

    public Task<IEnumerable<NatDevice>> DiscoverDevicesAsync(PortMapper portMapper, CancellationTokenSource cancellationTokenSource)
    {
        return _discoverer.DiscoverDevicesAsync(PortMapper.Upnp | PortMapper.Pmp, cancellationTokenSource);
    }

    public async Task CreatePortMapAsync(NatDevice device, ushort port)
    {
        var mapping = new Mapping(Protocol.Tcp, port, port);
        await device.CreatePortMapAsync(mapping);
        _devices.Add(device);
    }

    public async Task ReleaseAll()
    {
        foreach (var device in _devices)
        {
            foreach (var mapping in await device.GetAllMappingsAsync())
            {
                await device.DeletePortMapAsync(mapping);
            }
        }
    }
}

