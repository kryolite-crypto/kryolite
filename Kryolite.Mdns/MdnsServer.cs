using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Kryolite.Mdns;

public class MdnsServer : IDisposable
{
    public const string MDNS_ADDR = "224.0.0.251";
    public const int MDNS_PORT = 5353;

    private static readonly IPAddress _mdnsAddr = IPAddress.Parse(MDNS_ADDR);
    private static readonly IPEndPoint _mdnsEndpoint = new(_mdnsAddr, MDNS_PORT);

    private readonly UdpClient _receiver;
    private readonly IPAddress _localAddress;
    private readonly int _localPort;
    private readonly string _hostname;
    private readonly string _serviceName;

    public MdnsServer(string serviceName, IPAddress localAddress, int localPort)
    {
        _receiver = new UdpClient(AddressFamily.InterNetwork);
        _receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _receiver.Client.Bind(new IPEndPoint(IPAddress.Any, MDNS_PORT));
        _receiver.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(_mdnsAddr));
        _receiver.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

        _localAddress = localAddress;
        _localPort = localPort;
        _hostname = Guid.NewGuid().ToString().Split('-')[0];
        _serviceName = serviceName.Trim('.');
    }

    public void StartListening(CancellationToken token)
    {
        var thread = new Thread(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var receive = await _receiver.ReceiveAsync(token);
                    var result = receive.Buffer;

                    Debug.WriteLine($"Received {result.Length} bytes");

                    if (!DnsPacket.TryParse(result, out var dnsPacket))
                    {
                        Debug.WriteLine("Parse failed");
                        continue;
                    }

                    Trace.WriteLine(dnsPacket);

                    if (!dnsPacket.QR && dnsPacket.Questions.Any(x => x.QName == _serviceName))
                    {
                        Answer(dnsPacket, receive.RemoteEndPoint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down
            }
            catch (SocketException sEx)
            {
                if (sEx.ErrorCode == 125)
                {
                    // Operation cancelled, do nothing
                    Debug.WriteLine(sEx);
                }
                else
                {
                    Console.WriteLine(sEx);
                }
            }
        });

        thread.Start();
    }

    private void Answer(DnsPacket dnsPacket, IPEndPoint remoteEndpoint)
    {
        Debug.WriteLine("Generate answer");

        var answerPacket = new DnsPacket
        {
            Id = dnsPacket.Id,
            QR = true,
            AA = true
        };

        answerPacket.AddPTR(_hostname, _serviceName);
        answerPacket.AddSRV(_hostname, _serviceName, (ushort)_localPort);
        answerPacket.AddTXT(_hostname, _serviceName);

        if (_localAddress.Equals(IPAddress.Any))
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up)
                .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses.Select(ip => ip.Address))
                .ToArray();

            if (addresses.Length == 0)
            {
                // If we didn't find any then use loopback
                addresses = [ IPAddress.Loopback ];
            }

            foreach (var address in addresses)
            {
                answerPacket.AddA(_hostname, address);
            }
        }
        else
        {
            answerPacket.AddA(_hostname, _localAddress);
        }

        var bytes = answerPacket.ToArray();

        if (dnsPacket.Questions.Any(x => x.UR))
        {
            _receiver.Send(bytes, bytes.Length, remoteEndpoint);
        }
        else
        {
            _receiver.Send(bytes, bytes.Length, _mdnsEndpoint);
        }

        Debug.WriteLine("Packet sent");
    }

    public void Dispose()
    {
        _receiver.Dispose();
    }
}
