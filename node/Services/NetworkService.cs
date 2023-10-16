using System.Net.NetworkInformation;
using System.Numerics;
using System.Reactive.Linq;
using System.Timers;
using DnsClient;
using Kryolite.EventBus;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.Search;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

public class NetworkService : BackgroundService
{
    private IServiceProvider ServiceProvider { get; }
    private IServer Server { get; }
    private IMeshNetwork MeshNetwork { get; }
    private IConfiguration Configuration { get; }
    private INetworkManager NetworkManager { get; }
    private ILogger<NetworkService> Logger { get; }
    private ILookupClient LookupClient { get; }
    private StartupSequence Startup { get; }

    private readonly MulticastService mdns;
    private readonly ServiceDiscovery serviceDiscovery;

    public NetworkService(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Server = serviceProvider.GetRequiredService<IServer>();
        MeshNetwork = serviceProvider.GetRequiredService<IMeshNetwork>();
        Configuration = serviceProvider.GetRequiredService<IConfiguration>();
        Logger = serviceProvider.GetRequiredService<ILogger<NetworkService>>();
        LookupClient = serviceProvider.GetRequiredService<ILookupClient>();
        Startup = serviceProvider.GetRequiredService<StartupSequence>();

        NetworkManager = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<INetworkManager>();

        MulticastService.IncludeLoopbackInterfaces = true;
        mdns = new MulticastService();
        serviceDiscovery = new ServiceDiscovery(mdns);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleanupTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30));

        cleanupTimer.AutoReset = true;
        cleanupTimer.Elapsed += HostCleanup;
        cleanupTimer.Enabled = true;

        var discoveryTimer = new System.Timers.Timer(TimeSpan.FromMinutes(10));

        discoveryTimer.AutoReset = true;
        discoveryTimer.Elapsed += PeerDiscovery;
        discoveryTimer.Enabled = true;

        MeshNetwork.PeerConnected += async (object? sender, PeerConnectedEventArgs args) => {
            discoveryTimer.Interval = TimeSpan.FromMinutes(10).TotalMilliseconds;

            if (sender is not Peer peer)
            {
                return;
            }

            Logger.LogInformation($"{peer.Uri.ToHostname()} connected");

            var nodeHost = new NodeHost(peer.Uri)
            {
                ClientId = peer.ClientId,
                LastSeen = DateTime.UtcNow,
                IsReachable = peer.IsReachable
            };

            NetworkManager.AddHost(nodeHost);

            if (peer.ConnectionType == ConnectionType.OUT)
            {
                Logger.LogInformation($"Send NodeInfoRequest");
                await peer.SendAsync(new NodeInfoRequest());
            }
        };

        MeshNetwork.PeerDisconnected += async (object? sender, PeerDisconnectedEventArgs args) => {
            if (sender is not Peer peer)
            {
                return;
            }

            Logger.LogInformation($"{peer.Uri.ToHostname()} disconnected");

            var peerCount = MeshNetwork.GetPeers().Count;

            if (peerCount >= Constant.MAX_PEERS)
            {
                return;
            }

            // prevent connection spam when only a few nodes are available
            Thread.Sleep(TimeSpan.FromSeconds(1));

            if (peer.ConnectionType == ConnectionType.OUT)
            {
                var peers = MeshNetwork.GetPeers();

                var randomized = NetworkManager.GetHosts()
                    .Where(x => !peers.ContainsKey(x.ClientId))
                    .Where(x => x.IsReachable)
                    .Where(x => !x.IsBanned())
                    .OrderBy(x => Guid.NewGuid())
                    .ToList();

                foreach (var nextPeer in randomized)
                {
                    await MeshNetwork.ConnectToAsync(nextPeer.Url);

                    if (MeshNetwork.GetPeers().Count >= Constant.MAX_PEERS)
                    {
                        break;
                    }
                }
            }

            if (MeshNetwork.GetPeers().Count == 0)
            {
                Logger.LogWarning("All peers disconnected, trying to reconnect..");

                discoveryTimer.Interval = TimeSpan.FromSeconds(30).TotalMilliseconds;

                await DiscoverPeers();
            }
        };

        MeshNetwork.MessageReceived += (object? sender, MessageReceivedEventArgs args) =>
        {
            try
            {
                if (sender is not Peer peer) 
                {
                    Logger.LogWarning("Message received from unknown source");
                    return;
                }

                if (args.Message.Payload is not IPacket packet) 
                {
                    Logger.LogWarning("Invalid payload type received {}", args.Message.Payload?.GetType());
                    return;
                }

                packet.Handle(peer, args, ServiceProvider);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error while handling packet {}", args.Message.Payload?.GetType());
            }
        };

        Logger.LogInformation("Network       [UP]");

        await Task.Run(() => Startup.Application.Wait(stoppingToken));

        await DiscoverPeers();

        NetworkChange.NetworkAvailabilityChanged += new
            NetworkAvailabilityChangedEventHandler(NetworkAvailabilityChanged);

        Logger.LogInformation("Network       [UP]");
    }

    private async Task<List<string>> DownloadPeerList()
    {
        try
        {
            Logger.LogInformation("Resolving peers from testnet.kryolite.io");

            var result = await LookupClient.QueryAsync("testnet.kryolite.io", QueryType.TXT);

            if (result.HasError)
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }

            var peers = new List<Uri>();

            foreach (var txtRecord in result.Answers.TxtRecords().SelectMany(x => x.Text))
            {
                Logger.LogInformation($"Peer: {txtRecord}");

                var uriBuilder = new UriBuilder(txtRecord);
                peers.Add(uriBuilder.Uri);
            }

            var ret = new List<string>(peers.Select(x => x.ToHostname()));

            foreach (var peer in peers)
            {
                var list = await MeshNetwork.DownloadPeerListAsync(peer);

                if (list.Count > 0)
                {
                    Logger.LogInformation($"Downloaded {list.Count} peers from {peer.ToHostname()}");

                    foreach (var url in list)
                    {
                        // Convert to uri to make sure it is valid, wel also want to have string ending with / to be consistent
                        if (Uri.TryCreate(url, new UriCreationOptions(), out var uri))
                        {
                            ret.Add(uri.ToHostname());
                        }
                    }

                    break;
                }
            }

            return ret.Distinct().OrderBy(x => Guid.NewGuid()).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to download peer list");
            return new();
        }
    }

    private async Task DiscoverPeers()
    {
        var publicUrl = Configuration.GetValue<string?>("PublicUrl");
        var peers = Configuration.GetSection("Peers").Get<List<string>>() ?? new List<string>();

        if (peers.Count == 0)
        {
            peers.AddRange(await DownloadPeerList());
        }

        peers = peers
            .Where(x => x != publicUrl)
            .Where (x => !NetworkManager.IsBanned(x))
            .Distinct()
            .OrderBy(x => Guid.NewGuid())
            .ToList();

        foreach (var url in peers)
        {
            if (!Uri.TryCreate(url, new UriCreationOptions(), out var peerUri))
            {
                Logger.LogWarning("Invalid uri format {}", url);
                continue;
            }

            await MeshNetwork.ConnectToAsync(peerUri);

            if (MeshNetwork.GetPeers().Count >= Constant.MAX_PEERS)
            {
                break;
            }
        }

        if (MeshNetwork.GetPeers().Count == 0)
        {
            Logger.LogInformation("No peers resolved. Manually add peers in configuration.");
        }
    }

    private async void PeerDiscovery(object? sender, ElapsedEventArgs e)
    {
        var peers = MeshNetwork.GetPeers();

        if (peers.Count() < Constant.MAX_PEERS)
        {
            await DiscoverPeers();
        }

        var inPeers = peers.Values.Where(x => x.ConnectionType == ConnectionType.IN);
        var outPeers = peers.Values.Where(x => x.ConnectionType == ConnectionType.OUT);

        Logger.LogInformation($"Connected to {peers.Count} peers [in = {inPeers.Count()}, out = {outPeers.Count()}]");
    }

    private void HostCleanup(object? sender, ElapsedEventArgs e)
    {
        if (MeshNetwork.GetPeers().Count == 0)
        {
            // not connected to network, do ping hosts
            return;
        }

        Logger.LogInformation("Cleaning up stale nodes");
        var hosts = NetworkManager.GetHosts();

        foreach (var host in hosts)
        {
            if(!Connection.TestConnection(host.Url))
            {
                Logger.LogDebug($"Host {host.Url} not reachable, removing host");
                NetworkManager.RemoveHost(host);
            }
        }
    }

    private async void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Logger.LogInformation($"Network status changed, new status: {e.IsAvailable}");

        if (e.IsAvailable)
        {
            Logger.LogInformation($"Network connected, restoring peer connections");

            var peers = NetworkManager.GetHosts()
                .Where(x => x.IsReachable)
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            foreach (var peer in peers)
            {
                await MeshNetwork.ConnectToAsync(peer.Url);

                if (MeshNetwork.GetPeers().Count >= Constant.MAX_PEERS)
                {
                    break;
                }
            }
        }
    }
}

public class Chain
{
    public Peer Peer { get; set; }
    public long Height { get; set; }

    public Chain(Peer peer, long height)
    {
        Peer = peer ?? throw new ArgumentNullException(nameof(peer));
        Height = height;
    }
}

public class SyncProgress : EventBase
{
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public bool Completed { get; set; }
}
