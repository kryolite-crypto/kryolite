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
    private readonly IStoreManager blockchainManager;
    private readonly ILogger<NetworkService> logger;
    private readonly IEventBus eventBus;

    // TODO: Make better Sync state control
    public static bool InProgress;

    public ChainObserver(IServiceProvider serviceProvider)
    {
        this.nodeNetwork = nodeNetwork ?? throw new ArgumentNullException(nameof(nodeNetwork));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(logger));
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
        /*if (chain.Blocks.Count == 0)
        {
            return;
        }

        InProgress = true;

        logger.LogInformation($"Starting chain sync (blocks={chain.Blocks.Count}) (chain from node {chain.Peer.Uri.ToHostname()})");

        BeginSync?.Invoke(this, chain.Blocks.Count);

        var sortedBlocks = chain.Blocks.OrderBy(x => x.Height).ToList();

        sortedBlocks = FilterCommonBlocks(sortedBlocks);

        if (sortedBlocks.Count == 0)
        {
            EndSync?.Invoke(this, EventArgs.Empty);
            InProgress = false;
            return;
        }

        if (!VerifyChainIntegrity(sortedBlocks, out var remoteWorkToAdd))
        {
            _ = chain.Peer.DisconnectAsync();
            EndSync?.Invoke(this, EventArgs.Empty);
            InProgress = false;
            return;
        }

        if (!VerifyProofOfWork(sortedBlocks))
        {
            _ = chain.Peer.DisconnectAsync();
            EndSync?.Invoke(this, EventArgs.Empty);
            InProgress = false;
            return;
        }

        var localWorkAtRemoteHeight = CalculateLocalWorkAtRemoteHeight(sortedBlocks);

        var localWork = blockchainManager.GetTotalWork();
        var remoteWork = (localWork - localWorkAtRemoteHeight) + remoteWorkToAdd;

        logger.LogInformation($"Current chain totalWork = {localWork}, received chain with totalWork = {remoteWork}");

        if (remoteWork > localWork)
        {
            logger.LogInformation("Chain is ahead, rolling forward");

            if (!blockchainManager.SetChain(sortedBlocks))
            {
                logger.LogWarning("Failed to set chain, discarding...");
                EndSync?.Invoke(this, EventArgs.Empty);
                _ = chain.Peer.DisconnectAsync();
                InProgress = false;
                return;
            }

            // Query for more blocks
            _ = chain.Peer.SendAsync(new QueryNodeInfo());
        }

        logger.LogInformation($"Chain sync finished");
        EndSync?.Invoke(this, EventArgs.Empty);
        InProgress = false;*/
    }

    /*private BigInteger CalculateLocalWorkAtRemoteHeight(List<PosBlock> sortedBlocks)
    {
        var blocksOnSameHeight = blockchainManager.GetPowFrom(sortedBlocks.First().Height).Take(sortedBlocks.Count);
        var localWork = new BigInteger();

        foreach (var block in blocksOnSameHeight)
        {
            localWork += block.Difficulty.ToWork();
        }

        return localWork;
    }

    private List<PosBlock> FilterCommonBlocks(List<PosBlock> sortedBlocks)
    {
        long progress = 0;
        ReportProgress("Filter common blocks", progress, sortedBlocks.Count);

        var current = blockchainManager.GetPosFrom(sortedBlocks.First().Height - 1)
            .OrderBy(x => x.Height)
            .ToList();
        var startIndex = 0;

        for (int i = 0; i < Math.Min(sortedBlocks.Count, current.Count()); i++)
        {
            ReportProgress("Filter common blocks", progress, sortedBlocks.Count);

            if(sortedBlocks[i].GetHash() != current[i].GetHash())
            {
                break;
            }

            startIndex++;
        }

        if (startIndex > 0)
        {
            logger.LogInformation($"{startIndex} blocks already exists in localdb, discarding..");
            sortedBlocks = sortedBlocks.Skip(startIndex).ToList();
        }

        return sortedBlocks;
    }

    private bool VerifyChainIntegrity(List<PosBlock> sortedBlocks, out BigInteger totalWork)
    {
        long progress = 0;
        ReportProgress("Verifying chain integrity", progress, sortedBlocks.Count);

        var blockchainContext = new BlockchainExContext()
        {
            LastBlocks = blockchainManager.GetLastBlocks(sortedBlocks.First().Height, 11)
                .OrderBy(x => x.Height)
                .ToList()
        };

        totalWork = new BigInteger(0);

        var blockExecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext, logger)
            .Link<VerifyId>(x => x.Height > 0)
            .Link<VerifyParentHash>(x => x.Height > 0);

        foreach (var block in sortedBlocks)
        {
            if (block.Pow is not null)
            {
                if (!blockExecutor.Execute(block.Pow, out var result))
                {
                    logger.LogError($"Chain failed at {block.Height} ({result})");
                    return false;
                }

                totalWork += block.Pow.Difficulty.ToWork();
                blockchainContext.LastBlocks.Add(block.Pow);
            }

            ReportProgress("Verifying chain integrity", ++progress, sortedBlocks.Count);
        }

        return true;
    }

    private bool VerifyProofOfWork(List<PosBlock> sortedBlocks)
    {
        long progress = 0;
        ReportProgress("Verifying Proof-of-Work", progress, sortedBlocks.Count);

        var source = new CancellationTokenSource();
        var token = source.Token;

        Parallel.ForEach(sortedBlocks, block => {
            if (block.Pow is not null)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (!block.Pow.VerifyNonce())
                {
                    logger.LogError($"Invalid nonce at {block.Height}");
                    source.Cancel();
                }
            }

            ReportProgress("Verifying Proof-of-Work", Interlocked.Increment(ref progress), sortedBlocks.Count);
        });

        return !source.IsCancellationRequested;
    }*/
}
