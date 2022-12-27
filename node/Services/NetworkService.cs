using System.Numerics;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

public class NetworkService : BackgroundService
{
    private readonly IMeshNetwork meshNetwork;
    private readonly IConfiguration configuration;
    private readonly StartupSequence startup;
    private readonly INetworkManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly IMempoolManager mempoolManager;
    private readonly ILogger<NetworkService> logger;
    private readonly BufferBlock<BaseNode> SyncBuffer = new BufferBlock<BaseNode>();

    public NetworkService(IMeshNetwork meshNetwork, IConfiguration configuration, StartupSequence startup, ILogger<NetworkService> logger, INetworkManager networkManager, IBlockchainManager blockchainManager, IMempoolManager mempoolManager)
    {
        this.meshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() => startup.Blockchain.WaitOne());
        await Task.Run(() => startup.Mempool.WaitOne());

        SyncBuffer.AsObservable().Subscribe(new ChainObserver(meshNetwork, blockchainManager, logger));

        var discoveryBuffer = new BufferBlock<NodeCandidate>();
        var voteBuffer = new BufferBlock<Vote>();

        meshNetwork.ClientDropped += async (object? sender, EventArgs args) => {
            if (sender is not Peer peer) {
                // we only care about outgoing connections
                return;
            }

            if (peer.ForceDisconnect)
            {
                // Peer got disconnected by action made within node, do not reconnect to new node
                return;
            }

            var randomized = networkManager.GetHosts()
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            bool success = false;

            foreach (var host in randomized)
            {
                if (await meshNetwork.AddNode(host.Hostname, false, host.ClientId))
                {
                    success = true;
                    break;
                }
            }

            if (!success)
            {
                var msg = new Message
                {
                    Payload = new NodeDiscovery()
                };

                await meshNetwork.BroadcastAsync(msg);
            }
        };

        meshNetwork.MessageReceived += async (object? sender, MessageEventArgs args) =>
        {
            if (args.Message.Payload is null) 
            {
                logger.LogError("Empty payload received");
                return;
            }

            if (sender is not BaseNode node) 
            {
                logger.LogError("Message received from unknown source");
                return;
            }

            switch (args.Message.Payload) 
            {
                case NodeInfo nodeInfo:
                    logger.LogInformation($"Received NodeInfo from {args.Message.NodeId}");

                    var nodeHost = new NodeHost
                    {
                        Hostname = args.Hostname,
                        NodeInfo = nodeInfo
                    };

                    networkManager.AddHost(nodeHost);

                    var msg = new Message
                    {
                        Payload = new RequestChainSync
                        {
                            StartBlock = blockchainManager.GetCurrentHeight(),
                            StartHash = blockchainManager.GetLastBlockhash()
                        }
                    };

                    if (nodeInfo.TotalWork > blockchainManager.GetTotalWork())
                    {
                        await node.SendAsync(msg);
                        goto sync;
                    }

                    if (!Enumerable.SequenceEqual((byte[])nodeInfo.LastHash, (byte[])(blockchainManager.GetLastBlockhash() ?? new byte[0]))) {
                        goto sync;
                    }

                    return;
sync:
                    await node.SendAsync(msg);
                    break;
                case Blockchain blockchain:
                    logger.LogInformation($"Received blockchain from {args.Message.NodeId}");

                    if (blockchain.Blocks != null) 
                    {
                        node.Blockchain = blockchain.Blocks;
                        SyncBuffer.Post<BaseNode>(node);
                    }

                    break;
                case PosBlock newBlock:
                    logger.LogInformation($"Received block {newBlock.Height} from {args.Message.NodeId}");
                    var chainState = blockchainManager.GetChainState();

                    if (chainState.POS.Height > newBlock.Height)
                    {
                        return;
                    }

                    if (chainState.POS.Height < (newBlock.Height - 1))
                    {
                        var msg2 = new Message
                        {
                            Payload = new RequestChainSync
                            {
                                StartBlock = chainState.POS.Height,
                                StartHash = chainState.POS.LastHash
                            }
                        };

                        await node.SendAsync(msg2);
                        return;
                    }

                    if (blockchainManager.AddBlock(newBlock, false, true)) 
                    {
                        args.Rebroadcast = true;
                    }

                    break;
                case QueryNodeInfo queryNodeInfo:
                    logger.LogInformation($"Node query received from {args.Message.NodeId}");

                    var chainState2 = blockchainManager.GetChainState();
                    var response = new Message
                    {
                        Payload = new NodeInfo
                        {
                            Height = chainState2.POS.Height,
                            TotalWork = chainState2.POW.TotalWork,
                            LastHash = blockchainManager.GetLastBlockhash() ?? new SHA256Hash(),
                            CurrentTime = DateTime.UtcNow,
                            ConnectedPeers = meshNetwork.GetPeers().Count
                        }
                    };

                    await node.SendAsync(response);

                    networkManager.AddHost(new NodeHost
                    {
                        Hostname = $"{args.Hostname.Split(':').First()}:{queryNodeInfo.Port}",
                        ClientId = args.Message.NodeId,
                        LastSeen = DateTime.Now
                    });

                    break;
                case RequestChainSync syncParams:
                    logger.LogInformation($"Chain sync requested from {args.Message.NodeId}");

                    var block = blockchainManager.GetPosBlock(syncParams.StartBlock);

                    var chain = new Blockchain();

                    if (block == null) {
                        chain.Blocks = blockchainManager.GetPosFrom(0);
                        goto answer;
                    }

                    if (!Enumerable.SequenceEqual(block.GetHash().Buffer, syncParams.StartHash!)) {
                        chain.Blocks = blockchainManager.GetPosFrom(0);
                        goto answer;
                    }

                    if (syncParams.StartBlock == blockchainManager.GetCurrentHeight()) {
                        return;
                    }

                    chain.Blocks = blockchainManager.GetPosFrom(syncParams.StartBlock);
    answer:
                    var answer = new Message()
                    {
                        Payload = chain
                    };

                    await node.SendAsync(answer);
                    break;
                case TransactionData transactionData:
                    if (transactionData.Transactions.Count == 0) {
                        return;
                    }

                    logger.LogInformation($"Received {transactionData.Transactions.Count} transactions from {args.Message.NodeId}");

                    var valid = blockchainManager.AddTransactionsToQueue(transactionData.Transactions);

                    args.Message.Payload = new TransactionData
                    {
                        Transactions = valid
                    };

                    if (valid.Count > 0)
                    {
                        args.Rebroadcast = true;
                    }
                    break;
                case VoteBatch voteBatch:
                    logger.LogDebug($"Received {voteBatch.Votes.Count} votes from {args.Message.NodeId}");

                    var validVotes = blockchainManager.AddVotes(voteBatch.Votes);

                    foreach (var vote in validVotes)
                    {
                        voteBuffer.Post(vote);
                    }

                    break;
                case NodeDiscovery dicovery:
                    logger.LogInformation($"Received NodeDiscovery from {args.Message.NodeId}");

                    var elapsed = DateTime.UtcNow - networkManager.GetNetworkTime();
                    if (elapsed.TotalMinutes < 60 || networkManager.GetHosts().Count > 50) {
                        // send cached hosts
                        var answer2 = new Message
                        {
                            Payload = new NodeList
                            {
                                Nodes = networkManager.GetHosts().Select(x => new NodeCandidate
                                {
                                    Hostname = x.Hostname,
                                    ClientId = x.ClientId,
                                    ConnectedPeers = x.NodeInfo?.ConnectedPeers ?? 0
                                }).ToList()
                            }
                        };

                        await node.SendAsync(answer2);
                        return;
                    }

                    var ip = configuration.GetValue<string>("NodeIp") ?? "127.0.0.1";
                    var port = configuration.GetValue<int>("NodePort");
                    var hostname2 = $"http://{ip}:{port}";

                    // continue with fresh discovery
                    var selfHost = new NodeCandidate
                    {
                        Hostname = hostname2,
                        ClientId = MeshNetwork.ServerId,
                        ConnectedPeers = meshNetwork.GetPeers().Count
                    };

                    var adMsg = new Message
                    {
                        Payload = new NodeList { 
                            Nodes = new List<NodeCandidate> { selfHost }
                        }
                    };

                    await meshNetwork.BroadcastAsync(adMsg);
                    args.Rebroadcast = true;
                    break;
                case NodeList nodeList:
                    logger.LogInformation($"Received NodeList from ${args.Message.NodeId}");

                    foreach (var node2 in nodeList.Nodes)
                    {
                        discoveryBuffer.Post(node2);
                    }

                    break;
                default:
                    logger.LogError($"Invalid payload type: {args.Message.Payload.GetType()}");
                    break;
            }
        };

        blockchainManager.OnBlockAdded(new ActionBlock<PosBlock>(async block => {
            var msg = new Message
            {
                Payload = block
            };

            await meshNetwork.BroadcastAsync(msg);
        }));

        blockchainManager.OnVoteAdded(new ActionBlock<Vote>(async vote => {
            var msg = new Message
            {
                Payload = new VoteBatch
                {
                    Votes = new List<Vote> { vote }
                }
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

                var msg = new Message
                {
                    Payload = new TransactionData
                    {
                        Transactions = transactions
                    }
                };

                await meshNetwork.BroadcastAsync(msg);
            });

        mempoolManager.OnTransactionAdded(transactionBuffer);

        discoveryBuffer.AsObservable()
            .Buffer(TimeSpan.FromSeconds(1))
            .Subscribe(async nodes => {
                if (nodes.Count == 0)
                {
                    return;
                }

                logger.LogInformation($"Discovery found {nodes.Count} nodes");

                var current = networkManager.GetHosts().Select(x => new NodeCandidate
                {
                    Hostname = x.Hostname,
                    ClientId = x.ClientId,
                    ConnectedPeers = x.NodeInfo?.ConnectedPeers ?? 0
                }).ToList();

                current.AddRange(nodes);

                var randomized = nodes.Select(x => x.Hostname).Distinct().OrderBy(arg => Guid.NewGuid());

                var connected = 0;
                
                foreach (var hostname in randomized)
                {
                    Console.WriteLine($"Connecting to {hostname}");
                    if(!await meshNetwork.AddNode(hostname, false, Guid.Empty))
                    {
                        continue;
                    }

                    connected++;

                    if (connected == 6) // TODO: max peers
                    {
                        break;
                    }
                }
            });

        voteBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(100))
            .Subscribe(async votes => {
                if (votes.Count == 0)
                {
                    return;
                }

                var msg = new Message
                {
                    Payload = new VoteBatch
                    {
                        Votes = votes
                    }
                };

                await meshNetwork.BroadcastAsync(msg);
            });

        meshNetwork.Start();

        logger.LogInformation("Reading peers from appsettings.json");
        var peers = configuration.GetSection("Peers").Get<string[]>() ?? new string[0];

        foreach (var peer in peers)
        {
            var uri = new Uri(peer);

            if (await meshNetwork.AddNode(uri.Host, uri.Port, false, Guid.Empty))
            {
                var msg = new Message{
                    Payload = new NodeDiscovery()
                };

                await meshNetwork.BroadcastAsync(msg);
            }
        };

        logger.LogInformation("Network \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        startup.Network.Set();
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
            SignedBy = new Shared.PublicKey { Buffer = new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }},
            Signature = new Signature { Buffer = new byte[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0  }}
        };

        return blockchainManager.AddBlock(posBlock, true, true);
    }
}

[MessagePackObject]
public class Blockchain
{
    [Key(0)]
    public List<PosBlock>? Blocks { get; set; }
}

[MessagePackObject]
public class TransactionData
{
    [Key(0)]
    public IList<Transaction> Transactions { get; set; } = new List<Transaction>();
}

[MessagePackObject]
public class VoteBatch
{
    [Key(0)]
    public IList<Vote> Votes { get; set; } = new List<Vote>();
}

public class ChainObserver : IObserver<BaseNode>
{
    private readonly IMeshNetwork nodeNetwork;
    private readonly IBlockchainManager blockchainManager;
    private readonly ILogger<NetworkService> logger;

    // TODO: quick hack, create proper events
    public static event EventHandler<long>? BeginSync;
    public static event EventHandler<double>? SyncProgress;
    public static event EventHandler<EventArgs>? EndSync;

    public ChainObserver(IMeshNetwork nodeNetwork, IBlockchainManager blockchainManager, ILogger<NetworkService> logger)
    {
        this.nodeNetwork = nodeNetwork ?? throw new ArgumentNullException(nameof(nodeNetwork));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void ReportProgress(double progress, double total)
    {
        SyncProgress?.Invoke(null, progress / total * 100d);
    }
    
    public void OnCompleted()
    {
        
    }

    public void OnError(Exception error)
    {
        // TODO: log error
        throw new Exception("ChainObserver failed", error);
    }

    public async void OnNext(BaseNode node)
    {
        if (node.Blockchain == null || node.Blockchain.Count == 0) {
            return;
        }

        logger.LogInformation($"Starting chain sync (chain from node {node.ClientId})");

        BeginSync?.Invoke(this, node.Blockchain.Count);

        var sortedBlocks = node.Blockchain.OrderBy(x => x.Height).ToList();

        var min = sortedBlocks.Min(x => x.Height);

        BigInteger totalWork = new BigInteger(0);

        var blockchainContext = new BlockchainExContext()
        {
            LastBlocks = blockchainManager.GetLastBlocks(min, 11)
                .OrderBy(x => x.Id)
                .ToList()
        };

        var blockExecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext)
            .Link<VerifyNonce>()
            .Link<VerifyId>(x => x.Height > 0)
            .Link<VerifyParentHash>(x => x.Height > 0);

        long progress = 0;
        ReportProgress(progress, sortedBlocks.Count);

        foreach (var block in sortedBlocks)
        {
            if (block.Pow is not null)
            {
                if(!blockExecutor.Execute(block.Pow, out var result)) 
                {
                    // TODO: disconnect from node and remove from peers
                    logger.LogError($"Chain failed at {block.Height} ({result})");
                    return;
                }

                totalWork += block.Pow.Difficulty.ToWork();
                blockchainContext.LastBlocks.Add(block.Pow);
            }

            ReportProgress(++progress, sortedBlocks.Count);
        }

        if (totalWork > blockchainManager.GetTotalWork()) 
        {
            if(!blockchainManager.SetChain(sortedBlocks)) 
            {
                EndSync?.Invoke(this, EventArgs.Empty);
                return;
            }

            var msg = new Message
            {
                Payload = new QueryNodeInfo
                {
                    Port = nodeNetwork.GetPort()
                }
            };

            await node.SendAsync(msg);
        }

        EndSync?.Invoke(this, EventArgs.Empty);
    }
}
