using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeDiscovery : IPacket
{
    public async Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received NodeDiscovery from {args.Message.NodeId}");

        var elapsed = DateTime.UtcNow - context.NetworkManager.GetNetworkTime();
        if (elapsed.TotalMinutes < 60 || context.NetworkManager.GetHosts().Count > 50) {
            // send cached hosts
            var answer2 = new Message
            {
                Payload = new NodeList
                {
                    Nodes = context.NetworkManager.GetHosts()
                    .Where(x => x.IsReachable)
                    .Select(x => new NodeCandidate(x.Url)
                    {
                        ClientId = x.ClientId,
                        ConnectedPeers = x.NodeInfo?.ConnectedPeers ?? 0
                    }).ToList()
                }
            };

            await peer.SendAsync(answer2);
            return;
        }

        var uri = context.Configuration.GetSection("Kestrel").GetSection("Endpoints").GetSection("Http").GetValue<Uri>("Url") ?? new Uri("http://127.0.0.1:5000");
        
        var hostname2 = $"http://{uri.Host}:{uri.Port}";
        
        var publicUrl = context.Configuration.GetValue<Uri?>("PublicUrl");

        if (publicUrl == null)
        {
            var endpoints = context.Configuration.GetSection("Kestrel").GetSection("Endpoints").AsEnumerable();

            publicUrl = endpoints.Where(x => !string.IsNullOrEmpty(x.Value))
                .Select(x => new Uri(x.Value!))
                .FirstOrDefault(new Uri("http://localhost:5000"));
        }

        // continue with fresh discovery
        var selfHost = new NodeCandidate(publicUrl)
        {
            ClientId = MeshNetwork.ServerId,
            ConnectedPeers = context.MeshNetwork.GetPeers().Count
        };

        var adMsg = new Message
        {
            Payload = new NodeList { 
                Nodes = new List<NodeCandidate> { selfHost }
            }
        };

        await context.MeshNetwork.BroadcastAsync(adMsg);
        args.Rebroadcast = true;
    }
}
