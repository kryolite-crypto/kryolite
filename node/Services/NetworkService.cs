using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Threading.Tasks.Dataflow;
using System.Timers;
using DnsClient;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

public class NetworkService : BackgroundService
{
    private readonly IServer server;
    private readonly IMeshNetwork meshNetwork;
    private readonly IConfiguration configuration;
    private readonly StartupSequence startup;
    private readonly INetworkManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly ILogger<NetworkService> logger;
    private readonly BufferBlock<Chain> SyncBuffer = new BufferBlock<Chain>();
    private readonly ILookupClient LookupClient;
    private readonly MulticastService mdns;
    private readonly ServiceDiscovery serviceDiscovery;

    public NetworkService(IServer server, IMeshNetwork meshNetwork, IConfiguration configuration, StartupSequence startup, ILogger<NetworkService> logger, INetworkManager networkManager, IBlockchainManager blockchainManager, ILookupClient lookupClient)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.meshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        LookupClient = lookupClient ?? throw new ArgumentNullException(nameof(lookupClient));

        MulticastService.IncludeLoopbackInterfaces = true;
        mdns = new MulticastService();
        serviceDiscovery = new ServiceDiscovery(mdns);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.Blockchain.WaitOneAsync();

        var cleanupTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30));

        cleanupTimer.AutoReset = true;
        cleanupTimer.Elapsed += HostCleanup;
        cleanupTimer.Enabled = true;

        var discoveryTimer = new System.Timers.Timer(TimeSpan.FromMinutes(10));

        discoveryTimer.AutoReset = true;
        discoveryTimer.Elapsed += PeerDiscovery;
        discoveryTimer.Enabled = true;

        SyncBuffer.AsObservable().Subscribe(new ChainObserver(meshNetwork, blockchainManager, logger));

        var voteBuffer = new BufferBlock<Vote>();
        var context = new PacketContext(blockchainManager, networkManager, meshNetwork, configuration, logger, SyncBuffer, voteBuffer);

        meshNetwork.PeerConnected += async (object? sender, PeerConnectedEventArgs args) => {
            discoveryTimer.Interval = TimeSpan.FromMinutes(10).TotalMilliseconds;

            if (sender is not Peer peer)
            {
                return;
            }

            logger.LogInformation($"{peer.Uri.ToHostname()} connected");

            var nodeHost = new NodeHost(peer.Uri)
            {
                ClientId = peer.ClientId,
                LastSeen = DateTime.UtcNow,
                IsReachable = peer.IsReachable
            };

            networkManager.AddHost(nodeHost);

            if (peer.ConnectionType == ConnectionType.OUT)
            {
                await peer.SendAsync(new QueryNodeInfo());
            }
        };

        meshNetwork.PeerDisconnected += async (object? sender, PeerDisconnectedEventArgs args) => {
            if (sender is not Peer peer)
            {
                return;
            }

            logger.LogInformation($"{peer.Uri.ToHostname()} disconnected");

            var peerCount = meshNetwork.GetPeers().Count;

            if (peerCount >= Constant.MAX_PEERS)
            {
                return;
            }

            // prevent connection spam when only a few nodes are available
            Thread.Sleep(TimeSpan.FromSeconds(1));

            if (peer.ConnectionType == ConnectionType.OUT)
            {
                var peers = meshNetwork.GetPeers();

                var randomized = networkManager.GetHosts()
                    .Where(x => !peers.ContainsKey(x.ClientId))
                    .Where(x => x.IsReachable)
                    .OrderBy(x => Guid.NewGuid())
                    .ToList();

                foreach (var nextPeer in randomized)
                {
                    await meshNetwork.ConnectToAsync(nextPeer.Url);

                    if (meshNetwork.GetPeers().Count >= Constant.MAX_PEERS)
                    {
                        break;
                    }
                }
            }

            if (meshNetwork.GetPeers().Count == 0)
            {
                logger.LogWarning("All peers disconnected, trying to reconnect..");

                discoveryTimer.Interval = TimeSpan.FromSeconds(30).TotalMilliseconds;

                await DiscoverPeers();
            }
        };

        meshNetwork.MessageReceived += (object? sender, MessageReceivedEventArgs args) =>
        {
            try
            {
                if (sender is not Peer peer) 
                {
                    logger.LogWarning("Message received from unknown source");
                    return;
                }

                if (args.Message.Payload is not IPacket packet) 
                {
                    logger.LogWarning("Invalid payload type received {}", args.Message.Payload?.GetType());
                    return;
                }

                packet.Handle(peer, args, context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error while handling packet {}", args.Message.Payload?.GetType());
            }
        };

        blockchainManager.OnBlockAdded(new ActionBlock<Block>(async block => {
            await meshNetwork.BroadcastAsync(new NewBlock(block));
        }));

        blockchainManager.OnVoteAdded(new ActionBlock<Vote>(async signature => {
            var msg = new VoteBatch
            {
                Votes = new List<Vote> { signature }
            };

            await meshNetwork.BroadcastAsync(msg);
        }));

        var transactionBuffer = new BufferBlock<Transaction>();

        transactionBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(100), Constant.MAX_BLOCK_TX)
            .Subscribe(async transactions => {
                if (transactions.Count() == 0)
                {
                    return;
                }

                var msg = new TransactionData
                {
                    Transactions = transactions
                };

                await meshNetwork.BroadcastAsync(msg);
            });

        voteBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(100))
            .Subscribe(async signatures => {
                if (signatures.Count == 0)
                {
                    return;
                }

                var msg = new VoteBatch
                {
                    Votes = signatures
                };

                await meshNetwork.BroadcastAsync(msg);
            });

        await startup.Application.WaitOneAsync();

        await DiscoverPeers();

        NetworkChange.NetworkAvailabilityChanged += new
            NetworkAvailabilityChangedEventHandler(NetworkAvailabilityChanged);

        logger.LogInformation("Network       [UP]");
        startup.Network.Set();
    }

    private async Task<List<string>> DownloadPeerList()
    {
        logger.LogInformation("Resolving peers from testnet.kryolite.io");

        var result = await LookupClient.QueryAsync("testnet.kryolite.io", QueryType.TXT);

        if (result.HasError)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        var peers = new List<Uri>();

        foreach (var txtRecord in result.Answers.TxtRecords().SelectMany(x => x.Text))
        {
            logger.LogInformation($"Peer: {txtRecord}");

            var uriBuilder = new UriBuilder(txtRecord);
            peers.Add(uriBuilder.Uri);
        }

        var ret = new List<string>(peers.Select(x => x.ToString()));

        foreach (var peer in peers)
        {
            var list = await meshNetwork.DownloadPeerListAsync(peer);

            if (list.Count > 0)
            {
                logger.LogInformation($"Downloaded {list.Count} peers from {peer.ToHostname()}");

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
        var publicUrl = configuration.GetValue<string?>("PublicUrl");
        var peers = configuration.GetSection("Peers").Get<List<string>>() ?? new List<string>();

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
                logger.LogWarning("Invalid uri format {}", url);
                continue;
            }

            await meshNetwork.ConnectToAsync(peerUri);

            if (meshNetwork.GetPeers().Count >= Constant.MAX_PEERS)
            {
                break;
            }
        }

        if (meshNetwork.GetPeers().Count == 0)
        {
            logger.LogInformation("No peers resolved. Manually add peers in configuration.");
        }
    }

    private async void PeerDiscovery(object? sender, ElapsedEventArgs e)
    {
        var peers = meshNetwork.GetPeers();

        if (peers.Count() < Constant.MAX_PEERS)
        {
            await DiscoverPeers();
        }

        var inPeers = peers.Values.Where(x => x.ConnectionType == ConnectionType.IN);
        var outPeers = peers.Values.Where(x => x.ConnectionType == ConnectionType.OUT);

        logger.LogInformation($"Connected to {peers.Count} peers [in = {inPeers.Count()}, out = {outPeers.Count()}]");
    }

    private void HostCleanup(object? sender, ElapsedEventArgs e)
    {
        if (meshNetwork.GetPeers().Count == 0)
        {
            // not connected to network, do ping hosts
            return;
        }

        logger.LogInformation("Cleaning up stale nodes");
        var hosts = networkManager.GetHosts();

        foreach (var host in hosts)
        {
            if(!Connection.TestConnection(host.Url))
            {
                logger.LogDebug($"Host {host.Url} not reachable, removing host");
                networkManager.RemoveHost(host);
            }
        }
    }

    private async void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        logger.LogInformation($"Network status changed, new status: {e.IsAvailable}");

        if (e.IsAvailable)
        {
            logger.LogInformation($"Network connected, restoring peer connections");

            var peers = networkManager.GetHosts()
                .Where(x => x.IsReachable)
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            foreach (var peer in peers)
            {
                await meshNetwork.ConnectToAsync(peer.Url);

                if (meshNetwork.GetPeers().Count >= Constant.MAX_PEERS)
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
    public List<Block> Blocks { get; set; }

    public Chain(Peer peer, List<Block> blocks)
    {
        Peer = peer;
        Blocks = blocks;
    }
}

public class SyncEventArgs
{
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
}

public class ChainObserver : IObserver<Chain>
{
    private readonly IMeshNetwork nodeNetwork;
    private readonly IBlockchainManager blockchainManager;
    private readonly ILogger<NetworkService> logger;

    // TODO: quick hack, create proper events
    public static event EventHandler<long>? BeginSync;
    public static event EventHandler<SyncEventArgs>? SyncProgress;
    public static event EventHandler<EventArgs>? EndSync;

    // TODO: Make better Sync state control
    public static bool InProgress;

    public ChainObserver(IMeshNetwork nodeNetwork, IBlockchainManager blockchainManager, ILogger<NetworkService> logger)
    {
        this.nodeNetwork = nodeNetwork ?? throw new ArgumentNullException(nameof(nodeNetwork));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void ReportProgress(string status, double progress, double total)
    {
        if (SyncProgress is not null)
        {
            SyncProgress?.Invoke(null, new SyncEventArgs
            {
                Status = status,
                Progress = progress / total * 100d
            });
        }
    }
    
    public void OnCompleted()
    {

    }

    public void OnError(Exception error)
    {
        logger.LogError(error, "Chain sync failed");
        EndSync?.Invoke(this, EventArgs.Empty);
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
