using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Timers;
using DnsClient;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuikGraph;
using Redbus.Events;
using Redbus.Interfaces;
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
        NetworkManager = serviceProvider.GetRequiredService<INetworkManager>();
        Logger = serviceProvider.GetRequiredService<ILogger<NetworkService>>();
        LookupClient = serviceProvider.GetRequiredService<ILookupClient>();
        Startup = serviceProvider.GetRequiredService<StartupSequence>();

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
                await peer.SendAsync(new QueryNodeInfo());
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

        /*blockchainManager.OnVoteAdded(new ActionBlock<Vote>(async signature => {
            var msg = new VoteBatch
            {
                Votes = new List<Vote> { signature }
            };

            await meshNetwork.BroadcastAsync(msg);
        }));*/

        Logger.LogInformation("Network       [UP]");

        await Task.Run(() => Startup.Application.Wait(stoppingToken));

        await DiscoverPeers();

        NetworkChange.NetworkAvailabilityChanged += new
            NetworkAvailabilityChangedEventHandler(NetworkAvailabilityChanged);

        Logger.LogInformation("Network       [UP]");
    }

    private async Task<List<string>> DownloadPeerList()
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

        var ret = new List<string>(peers.Select(x => x.ToString()));

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
                        ret.Add(uri.ToString());
                    }
                }

                break;
            }
        }

        return ret.Distinct().OrderBy(x => Guid.NewGuid()).ToList();
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
    public List<TransactionDto> Transactions { get; set; }

    public Chain(Peer peer, List<TransactionDto> transactions)
    {
        Peer = peer;
        Transactions = transactions;
    }
}

public class SyncProgress : EventBase
{
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public bool Completed { get; set; }
}

public class ChainObserver : IObserver<Chain>
{
    private readonly IMeshNetwork nodeNetwork;
    private readonly IStoreManager storeManager;
    private readonly ILogger<NetworkService> logger;
    private readonly IEventBus eventBus;

    // TODO: Make better Sync state control
    public static bool InProgress;

    public ChainObserver(IServiceProvider serviceProvider)
    {
        nodeNetwork = serviceProvider.GetRequiredService<IMeshNetwork>();
        storeManager = serviceProvider.GetRequiredService<IStoreManager>();
        logger = serviceProvider.GetRequiredService<ILogger<NetworkService>>();
        eventBus = serviceProvider.GetRequiredService<IEventBus>();
    }

    public void ReportProgress(string status, double progress, double total)
    {
        eventBus.Publish(new SyncProgress
        {
            Status = status,
            Progress = progress / total * 100d
        });
    }
    
    public void OnCompleted()
    {

    }

    public void OnError(Exception error)
    {
        logger.LogError(error, "Chain sync failed");

        eventBus.Publish(new SyncProgress
        {
            Status = "",
            Progress = 100
        });
    }

    public void OnNext(Chain chain)
    {
        if (chain.Transactions.Count == 0)
        {
            return;
        }

        InProgress = true;

        logger.LogInformation($"Starting chain sync (transactions={chain.Transactions.Count}) (chain from node {chain.Peer.Uri.ToHostname()})");

        foreach (var tx in chain.Transactions)
        {
            foreach (var parent in tx.Parents)
            {
                if (!storeManager.Exists(parent))
                {
                    logger.LogInformation($"Chain failed, {tx.CalculateHash()} references unknown transaction ({parent})");
                    ReportProgress("", 0, 0);
                    InProgress = false;
                    return;
                }
            }
        }

        var localHeight = storeManager.GetCurrentHeight();

        var minRemoteHeight = chain.Transactions.Where(x => x.TransactionType == TransactionType.VIEW)
            .Select(x => BitConverter.ToInt64(x.Data))
            .DefaultIfEmpty()
            .Min();


        TransactionDto? view = null;

        var chainState = storeManager.GetChainStateAt(Math.Max(minRemoteHeight - 1, 0))!;
        var voteCount = 0;
        var blockCount = 0;

        var graph = chain.Transactions.AsGraph();

        foreach (var vertex in graph.Vertices)
        {
            foreach (var edge in graph.OutEdges(vertex).DistinctBy(x => x.Target))
            {
                var tx = edge.Tag;

                switch (tx.TransactionType)
                {
                    case TransactionType.VIEW:
                        if (view is not null)
                        {
                            // TODO: Similar implementation exists in StoreManager
                            if (blockCount == 0)
                            {
                                var work = chainState.CurrentDifficulty.ToWork();
                                var nextTarget = work / 4 * 3;
                                var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                                chainState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                            }
                            else
                            {
                                var totalWork = chainState.CurrentDifficulty.ToWork() * blockCount;

                                chainState.Weight += totalWork;
                                chainState.CurrentDifficulty = totalWork.ToDifficulty();
                            }

                            chainState.Height++;
                        }

                        voteCount = 0;
                        blockCount = 0;

                        view = tx;

                        break;
                    case TransactionType.BLOCK:
                        blockCount++;
                        break;
                    case TransactionType.VOTE:
                        voteCount++;
                        break;
                }

                // Stop here to compare weight at same heights
                if (chainState.Height >= localHeight)
                {
                    break;
                }
            }
        }

        var localWeight = storeManager.GetChainState()?.Weight ?? BigInteger.Zero;

        logger.LogInformation($"Local chain weight = {localWeight}, remote chain weight = {chainState.Weight}");

        if (chainState.Height > localWeight)
        {
            logger.LogInformation("Chain is ahead, rolling forward");

            if (!storeManager.SetChain(graph, minRemoteHeight))
            {
                logger.LogInformation("Failed to set chain, discarding...");

                _ = chain.Peer.DisconnectAsync();

                ReportProgress("", 0, 0);
                InProgress = false;
                return;
            }

            // Query for next set
            _ = chain.Peer.SendAsync(new QueryNodeInfo());
        }

        logger.LogInformation($"Chain sync finished");
        ReportProgress("", 0, 0);
        InProgress = false;
    }
}
