using System.Net.NetworkInformation;
using System.Numerics;
using System.Reactive.Linq;
using System.Timers;
using Common.Logging;
using DnsClient;
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

public class ChainObserver : IObserver<Chain>, IDisposable
{
    private readonly IServiceScope scope;

    private readonly IMeshNetwork nodeNetwork;
    private readonly IStoreManager storeManager;
    private readonly ILogger<NetworkService> logger;
    private readonly IEventBus eventBus;
    private readonly INetworkManager networkManager;

    // TODO: Make better Sync state control
    public static bool InProgress;

    public ChainObserver(IServiceProvider serviceProvider)
    {
        scope = serviceProvider.CreateScope();
        nodeNetwork = scope.ServiceProvider.GetRequiredService<IMeshNetwork>();
        storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        logger = scope.ServiceProvider.GetRequiredService<ILogger<NetworkService>>();
        eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
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
        try
        {
            if (chain.Transactions.Count == 0)
            {
                return;
            }

            InProgress = true;

            logger.LogInformation($"Starting chain sync (transactions = {chain.Transactions.Count}) (chain from node {chain.Peer.Uri.ToHostname()})");

            var localState = storeManager.GetChainState();

            var maxCommonView = chain.Transactions
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .Where(x => storeManager.Exists(x.CalculateHash()))
                .MaxBy(x => BitConverter.ToInt64(x.Data));

            var maxCommonHeight = maxCommonView is not null ?
                BitConverter.ToInt64(maxCommonView.Data) - 1:
                localState.Height;

            logger.LogInformation($"Found common height at {maxCommonHeight}");

            var graph = chain.Transactions.AsGraph();
            var transactions = chain.Transactions.ToDictionary(x => x.CalculateHash(), y => y);

            chain.Transactions.Clear();

            if (maxCommonView is not null)
            {
                graph = FilterOutCommonVertices(maxCommonView, graph, transactions);
            }

            var remoteState = storeManager.GetChainStateAt(maxCommonHeight) ?? throw new Exception($"chain not found at height {maxCommonHeight}");

            var voteCount = 0;
            var blockCount = 0;
            var totalStake = 0L;

            foreach (var vertex in graph.TopologicalSort().Reverse())
            {
                var tx = transactions[vertex];

                switch (tx.TransactionType)
                {
                    case TransactionType.VIEW:
                        // TODO: Similar implementation exists in StoreManager
                        remoteState.Weight += remoteState.CurrentDifficulty.ToWork() * (totalStake / 1_000_000);
                        remoteState.LastHash = tx.CalculateHash();

                        if (blockCount == 0)
                        {
                            var work = remoteState.CurrentDifficulty.ToWork();
                            var nextTarget = work / 4 * 3;
                            var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                            remoteState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                        }
                        else
                        {
                            var totalWork = remoteState.CurrentDifficulty.ToWork() * blockCount;
                            remoteState.CurrentDifficulty = totalWork.ToDifficulty();
                        }

                        remoteState.Height++;

                        voteCount = 0;
                        blockCount = 0;
                        totalStake = 0;

                        break;
                    case TransactionType.BLOCK:
                        blockCount++;
                        break;
                    case TransactionType.VOTE:
                        voteCount++;

                        // Note: votes value must equal to signers stake, this is verified in Verifier
                        var stake = tx.Value;

                        if (Constant.SEED_VALIDATORS.Contains(tx.PublicKey!.ToAddress()))
                        {
                            stake = Constant.MIN_STAKE;
                        }

                        totalStake += stake;

                        break;
                }
            }

            var localWeight = localState?.Weight ?? BigInteger.Zero;

            logger.LogInformation($"Local chain weight = {localWeight}, remote chain weight = {remoteState.Weight}");

            if (remoteState.Weight > localWeight)
            {
                logger.LogInformation($"Merging remote chain from height #{maxCommonHeight} to #{remoteState.Height}");

                if (!storeManager.SetChain(graph, transactions, maxCommonHeight))
                {
                    logger.LogInformation("Failed to set chain, discarding...");

                    networkManager.Ban(chain.Peer.ClientId);
                    _ = chain.Peer.DisconnectAsync();

                    ReportProgress("", 0, 0);
                    InProgress = false;
                    return;
                }
            }
            else if (remoteState.LastHash == localState?.LastHash!)
            {
                logger.LogInformation($"Local and remote chains are equal. Applying pending transactions");

                if (!storeManager.AddTransactionBatch(transactions.Values.ToList(), false))
                {
                    logger.LogInformation("Failed to apply pending transactions...");

                    networkManager.Ban(chain.Peer.ClientId);
                    _ = chain.Peer.DisconnectAsync();

                    ReportProgress("", 0, 0);
                    InProgress = false;
                    return;
                }
            }


            // Query for next set
            _ = chain.Peer.SendAsync(new QueryNodeInfo());

            logger.LogInformation($"Chain sync finished");
            ReportProgress("", 0, 0);
            InProgress = false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chain Sync failed");
            ReportProgress("", 0, 0);
            InProgress = false;

            networkManager.Ban(chain.Peer.ClientId);
            _ = chain.Peer.DisconnectAsync();
        }
    }

    private static AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> FilterOutCommonVertices(TransactionDto maxCommonView, AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> graph, Dictionary<SHA256Hash, TransactionDto> transactions)
    {
        var newGraph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>();

        var bfs = new BreadthFirstSearchAlgorithm<SHA256Hash, Edge<SHA256Hash>>(graph);
        bfs.SetRootVertex(maxCommonView.CalculateHash());
        bfs.Compute();

        var newVertices = bfs.VisitedGraph.Vertices.Where(vertex => bfs.VerticesColors[vertex] == GraphColor.White);

        newGraph.AddVertexRange(newVertices);

        foreach (var vertex in bfs.VisitedGraph.TopologicalSort().Reverse())
        {
            // white == not visited
            if (bfs.VerticesColors[vertex] != GraphColor.White)
            {
                continue;
            }

            var tx = transactions[vertex];

            foreach (var parent in tx.Parents)
            {
                if (newGraph.ContainsVertex(parent))
                {
                    newGraph.AddEdge(new Edge<SHA256Hash>(tx.CalculateHash(), parent));
                }
            }
        }

        return newGraph;
    }

    public void Dispose()
    {
        scope.Dispose();
    }
}
