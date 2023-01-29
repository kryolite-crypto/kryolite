using System.Net;
using System.Net.Sockets;
using Zeroconf;

namespace Kryolite.Shared;

public static class ZeroConf
{
    public static async Task<string?> DiscoverNodeAsync()
    {
        try
        {
            var addresses = new HashSet<IPEndPoint>();
            var results = await ZeroconfResolver.ResolveAsync("_kryolite._tcp.local.");

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
                var uri = new UriBuilder()
                {
                    Host = addr.Address.ToString(),
                    Port = addr.Port
                };

                var success = await Connection.TestConnectionAsync(uri.Uri);

                if (!success)
                {
                    continue;
                }

                return $"http://{addr.Address}:{addr.Port}";
            }
        }
        catch (Exception)
        {

        }

        return null;
    }
}
