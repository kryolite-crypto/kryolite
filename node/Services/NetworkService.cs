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
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class NetworkService : BackgroundService
{
    private readonly IServer server;
    private readonly IMeshNetwork meshNetwork;
    private readonly IConfiguration configuration;
    private readonly StartupSequence startup;
    private readonly INetworkManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly IMempoolManager mempoolManager;
    private readonly ILogger<NetworkService> logger;
    private readonly BufferBlock<Chain> SyncBuffer = new BufferBlock<Chain>();
    private readonly ILookupClient LookupClient;
    private readonly MulticastService mdns;
    private readonly ServiceDiscovery serviceDiscovery;

    public NetworkService(IServer server, IMeshNetwork meshNetwork, IConfiguration configuration, StartupSequence startup, ILogger<NetworkService> logger, INetworkManager networkManager, IBlockchainManager blockchainManager, IMempoolManager mempoolManager, ILookupClient lookupClient)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.meshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        LookupClient = lookupClient ?? throw new ArgumentNullException(nameof(lookupClient));

        MulticastService.IncludeLoopbackInterfaces = true;
        mdns = new MulticastService();
        serviceDiscovery = new ServiceDiscovery(mdns);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.Blockchain.WaitOneAsync();
        await startup.Mempool.WaitOneAsync();

        SyncBuffer.AsObservable().Subscribe(new ChainObserver(meshNetwork, blockchainManager, logger));

        var voteBuffer = new BufferBlock<Vote>();
        var context = new PacketContext(blockchainManager, networkManager, meshNetwork, configuration, logger, SyncBuffer, voteBuffer);

        meshNetwork.PeerDisconnected += async (object? sender, PeerDisconnectedEventArgs args) => {
            if (sender is not Peer client) 
            {
                return;
            }

            var peerCount = meshNetwork.GetOutgoingConnections().Count();

            if (peerCount >= Constant.MAX_PEERS)
            {
                return;
            }

            var peers = meshNetwork.GetPeers();

            var randomized = networkManager.GetHosts()
                .Where(x => !peers.ContainsKey(x.ClientId))
                .Where(x => x.IsReachable)
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            foreach (var peer in randomized)
            {
                if (await meshNetwork.ConnectToAsync(peer.Url))
                {
                    peerCount++;
                }

                if (peerCount >= Constant.MAX_PEERS)
                {
                    break;
                }
            }

            if (meshNetwork.GetPeers().Count == 0)
            {
                logger.LogWarning("All peers disconnected, trying to reconnect..");
                ReconnectToNetwork();
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

        blockchainManager.OnBlockAdded(new ActionBlock<PosBlock>(async block => {
            await meshNetwork.BroadcastAsync(new NewBlock(block));
        }));

        blockchainManager.OnVoteAdded(new ActionBlock<Vote>(async vote => {
            var msg = new VoteBatch
            {
                Votes = new List<Vote> { vote }
            };

            await meshNetwork.BroadcastAsync(msg);
        }));

        networkManager.OnBlockProposed(new ActionBlock<PowBlock>(block => {
            ProposeBlock(block);
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

        mempoolManager.OnTransactionAdded(transactionBuffer);

        voteBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(100))
            .Subscribe(async votes => {
                if (votes.Count == 0)
                {
                    return;
                }

                var msg = new VoteBatch
                {
                    Votes = votes
                };

                await meshNetwork.BroadcastAsync(msg);
            });

        await startup.Application.WaitOneAsync();

        await DiscoverPeers();

        NetworkChange.NetworkAvailabilityChanged += new
            NetworkAvailabilityChangedEventHandler(NetworkAvailabilityChanged);

        var timer = new System.Timers.Timer(TimeSpan.FromMinutes(30));

        timer.AutoReset = true;
        timer.Elapsed += HostCleanup;
        timer.Enabled = true;

        var timer2 = new System.Timers.Timer(TimeSpan.FromMinutes(10));

        timer2.AutoReset = true;
        timer2.Elapsed += PeerDiscovery;
        timer2.Enabled = true;

        logger.LogInformation("Network       \x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        startup.Network.Set();
    }

    private async Task<List<string>> ResolveInitialPeers()
    {
        logger.LogInformation("Resolving peers from testnet.kryolite.io");

        var result = await LookupClient.QueryAsync("testnet.kryolite.io", QueryType.TXT);

        if (result.HasError)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        List<string> peers = new();

        foreach (var txtRecord in result.Answers.TxtRecords().SelectMany(x => x.Text))
        {
            logger.LogInformation($"Peer: {txtRecord}");

            var uriBuilder = new UriBuilder(txtRecord);
            peers.Add(uriBuilder.Uri.ToString().TrimEnd('/'));
        }

        return peers;
    }

    private async Task<bool> DiscoverPeers()
    {
        var peers = configuration.GetSection("Peers").Get<List<string>>() ?? new List<string>();

        if (peers.Count == 0)
        {
            peers.AddRange(await ResolveInitialPeers());
        }

        var hosts = networkManager.GetHosts()
            .Where(x => x.IsReachable)
            .Select(x => x.Url.ToString());

        peers.AddRange(hosts);

        peers = peers.OrderBy(x => Guid.NewGuid())
            .ToList();

        var connected = false;

        foreach (var url in peers.Distinct())
        {
            logger.LogInformation("Connecting to peer {}", url);

            if (!Uri.TryCreate(url, new UriCreationOptions(), out var peerUri))
            {
                logger.LogWarning("Invalid uri format {}", url);
                continue;
            }

            if (await meshNetwork.ConnectToAsync(peerUri))
            {
                var peer = meshNetwork.GetPeer(peerUri);

                if (peer != null)
                {
                    await peer.SendAsync(new QueryNodeList());
                    connected = true;
                }
            }

            if (peers.Count >= Constant.MAX_PEERS)
            {
                break;
            }
        }

        if (peers.Count == 0)
        {
            logger.LogInformation("No peers resolved. Manually add peers in configuration.");
            return false;
        }

        return connected;
    }

    private async void PeerDiscovery(object? sender, ElapsedEventArgs e)
    {
        if (meshNetwork.GetOutgoingConnections().Count() < Constant.MAX_PEERS)
        {
            await DiscoverPeers();
        }

        var peers = meshNetwork.GetPeers();
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

    private void ReconnectToNetwork()
    {
        _ = Task.Run(async () => {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            while (await timer.WaitForNextTickAsync())
            {
                if(await DiscoverPeers())
                {
                    break;
                }
            }
        });
    }

    private async void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        logger.LogInformation($"Network status changed, new status: {e.IsAvailable}");

        if (e.IsAvailable)
        {
            logger.LogInformation($"Network connected, restoring peer connections");

            var count = meshNetwork.GetOutgoingConnections().Count();
            var peers = networkManager.GetHosts()
                .Where(x => x.IsReachable)
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            foreach (var peer in peers)
            {
                if(await meshNetwork.ConnectToAsync(peer.Url))
                {
                    count++;
                }

                if (count >= Constant.MAX_PEERS)
                {
                    break;
                }
            }
        }
    }

    public bool ProposeBlock(PowBlock block)
    {
        logger.LogInformation($"Proposing POW block {block.Height} to network...");
        // TODO: lock

        // TODO: Validate

        var chainState = blockchainManager.GetChainState();

        var posBlock = new PosBlock
        {
            Height = chainState.POS.Height + 1,
            ParentHash = chainState.POS.LastHash,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Pow = block,
            SignedBy = new Shared.PublicKey(new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            Signature = new Signature(new byte[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0  })
        };

        return blockchainManager.AddBlock(posBlock, true, true);
    }
}

public class Chain
{
    public Peer Peer { get; set; }
    public List<PosBlock> Blocks { get; set; }

    public Chain(Peer peer, List<PosBlock> blocks)
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
        if (chain.Blocks.Count == 0)
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
        InProgress = false;
    }

    private BigInteger CalculateLocalWorkAtRemoteHeight(List<PosBlock> sortedBlocks)
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
    }
}
