using System.Net;
using Zeroconf;

namespace Kryolite.Shared;

public static class ZeroConf
{
    public static async Task<string?> DiscoverNodeAsync(int scanTime = 2)
    {
        try
        {
            var addresses = new HashSet<IPEndPoint>();
            var results = await ZeroconfResolver.ResolveAsync("_kryolite._tcp.local.", TimeSpan.FromSeconds(scanTime));

            foreach (var result in results)
            {
                foreach (var service in result.Services)
                {
                    foreach (var ipAddress in result.IPAddresses)
                    {
                        addresses.Add(new IPEndPoint(IPAddress.Parse(ipAddress), service.Value.Port));
                    }
                }
            }

            // prefer connecting to loopback addresses (local node)
            foreach (var addr in addresses.ToList().OrderBy(x => IPAddress.IsLoopback(x.Address) ? 0 : 1))
            {
                return $"http://{addr.Address}:{addr.Port}";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        return null;
    }
}
