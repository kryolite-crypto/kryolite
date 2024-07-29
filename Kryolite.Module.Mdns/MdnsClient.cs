using System.Net;
using System.Net.Sockets;

namespace Kryolite.Module.Mdns;

public class MdnsClient : IDisposable
{
    private UdpClient _client = new();
    
    public async Task<List<string>> Query(string serviceName, int timeout = 2000)
    {
        using var cts = new CancellationTokenSource();

        try
        {
            serviceName = serviceName.Trim('.');

            var mdnsAddr = IPAddress.Parse(MdnsServer.MDNS_ADDR);
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _client.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsServer.MDNS_PORT));
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(mdnsAddr));
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            var packet = new DnsPacket();
            packet.AddQuestion(serviceName, PTRRecord.TYPE);

            var tcs = new TaskCompletionSource<DnsPacket>(cts.Token);

            // Listen for responses
            _ = Task.Run(async () =>
            {
                var token = cts.Token;
                while (!token.IsCancellationRequested)
                {
                    var result = await _client.ReceiveAsync(cts.Token);

                    if (!DnsPacket.TryParse(result.Buffer, out var packet))
                    {
                        continue;
                    }

                    if (packet.QR && packet.Answers.Any(x => x.Name == serviceName))
                    {
                        tcs.SetResult(packet);
                    }
                }
            });

            // Send packet
            await _client.SendAsync(packet.ToArray(), new IPEndPoint(mdnsAddr, MdnsServer.MDNS_PORT));

            // Wait for result or until timeout
            var answer = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeout));

            // Parse ARecords
            var aRecords = answer.Answers
                .Where(x => x.Type == ARecord.TYPE)
                .Select(x => new { x.Name, A = ARecord.Parse(x.RData) })
                .ToList();

            // Parse SRVRecords, sort them by priority and weight and then join SRVRecords on ARecords to get http://ip:port uris
            var records = answer.Answers
                .Where(x => x.Type == SRVRecord.TYPE)
                .Select(x => SRVRecord.Parse(x.RData))
                .OrderBy(x => x.Priority)
                .ThenByDescending(x => x.Weight)
                .GroupJoin(aRecords, srv => srv.Target, b => b.Name, (srv, a) => new { SRVRecord = srv, ARecords = a })
                .SelectMany(x => x.ARecords.Select(y => $"http://{y.A.Target}:{x.SRVRecord.Port}"))
                .ToList();

            return records;
        }
        catch (TimeoutException)
        {
            return [];
        }
        finally
        {
            cts.Cancel();
        }
    }
    
    public void Dispose()
    {
        _client.Dispose();
    }
}