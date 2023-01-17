using System.Numerics;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using DnsClient;
using DnsClient.Protocol;
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
    private readonly BufferBlock<Peer> SyncBuffer = new BufferBlock<Peer>();
    private readonly ILookupClient LookupClient;

    public NetworkService(IMeshNetwork meshNetwork, IConfiguration configuration, StartupSequence startup, ILogger<NetworkService> logger, INetworkManager networkManager, IBlockchainManager blockchainManager, IMempoolManager mempoolManager, ILookupClient lookupClient)
    {
        this.meshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        LookupClient = lookupClient ?? throw new ArgumentNullException(nameof(lookupClient));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() => startup.Blockchain.WaitOne());
        await Task.Run(() => startup.Mempool.WaitOne());

        SyncBuffer.AsObservable().Subscribe(new ChainObserver(meshNetwork, blockchainManager, logger));

        var discoveryBuffer = new BufferBlock<NodeCandidate>();
        var voteBuffer = new BufferBlock<Vote>();

        var uri = configuration.GetSection("Kestrel").GetSection("Endpoints").GetSection("Http").GetValue<Uri>("Url") ?? new Uri("http://127.0.0.1:5000");

        meshNetwork.ClientDropped += async (object? sender, EventArgs args) => {
            if (sender is not LocalClient client) {
                // we only care about outgoing connections
                return;
            }

            if (client.ForceDisconnect)
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
                if (await meshNetwork.AddNode(host.Url, host.ClientId))
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

            if (sender is not Peer peer) 
            {
                logger.LogError("Message received from unknown source");
                return;
            }

            switch (args.Message.Payload) 
            {
                case NodeInfo nodeInfo:
                    logger.LogInformation($"Received NodeInfo from {args.Message.NodeId}");
                    var chainState2 = blockchainManager.GetChainState();

                    var nodeHost = new NodeHost(peer.Url)
                    {
                        NodeInfo = nodeInfo,
                        LastSeen = DateTime.Now
                    };

                    networkManager.AddHost(nodeHost);

                    var msg = new Message
                    {
                        Payload = new RequestChainSync
                        {
                            StartBlock = chainState2.POS.Height,
                            StartHash = chainState2.POS.LastHash
                        }
                    };

                    var totalWork = blockchainManager.GetTotalWork();

                    if (nodeInfo.TotalWork > totalWork)
                    {
                        logger.LogInformation($"{args.Message.NodeId} has greater TotalWork ({nodeInfo.TotalWork}) compared to local ({totalWork}). Requesting chain sync");
                        await peer.SendAsync(msg);
                    }

                    break;
                case Blockchain blockchain:
                    logger.LogInformation($"Received blockchain from {args.Message.NodeId}");

                    if (blockchain.Blocks != null) 
                    {
                        peer.Blockchain = blockchain.Blocks;
                        SyncBuffer.Post<Peer>(peer);
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
                        logger.LogInformation($"Chain is behind received block (local = {chainState.POS.Height}, received = {newBlock.Height}), requesting chain sync...");
                        var msg2 = new Message
                        {
                            Payload = new RequestChainSync
                            {
                                StartBlock = chainState.POS.Height,
                                StartHash = chainState.POS.LastHash
                            }
                        };

                        await peer.SendAsync(msg2);
                        return;
                    }

                    if (blockchainManager.AddBlock(newBlock, false, true)) 
                    {
                        args.Rebroadcast = true;
                    }

                    break;
                case QueryNodeInfo queryNodeInfo:
                    logger.LogInformation($"Node query received from {args.Message.NodeId}");

                    var chainState3 = blockchainManager.GetChainState();
                    var response = new Message
                    {
                        Payload = new NodeInfo
                        {
                            Height = chainState3.POS.Height,
                            TotalWork = chainState3.POW.TotalWork,
                            LastHash = blockchainManager.GetLastBlockhash() ?? new SHA256Hash(),
                            CurrentTime = DateTime.UtcNow,
                            ConnectedPeers = meshNetwork.GetPeers().Count
                        }
                    };

                    await peer.SendAsync(response);

                    networkManager.AddHost(new NodeHost(peer.Url)
                    {
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

                    await peer.SendAsync(answer);
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
                                Nodes = networkManager.GetHosts().Select(x => new NodeCandidate(x.Url)
                                {
                                    ClientId = x.ClientId,
                                    ConnectedPeers = x.NodeInfo?.ConnectedPeers ?? 0
                                }).ToList()
                            }
                        };

                        await peer.SendAsync(answer2);
                        return;
                    }

                    var hostname2 = $"http://{uri.Host}:{uri.Port}";

                    var publicUrl = configuration.GetValue<Uri?>("PublicUrl");

                    if (publicUrl == null)
                    {
                        var endpoints = configuration.GetSection("Kestrel").GetSection("Endpoints").AsEnumerable();

                        publicUrl = endpoints.Where(x => !string.IsNullOrEmpty(x.Value))
                            .Select(x => new Uri(x.Value!))
                            .FirstOrDefault(new Uri("http://localhost:5000"));
                    }

                    // continue with fresh discovery
                    var selfHost = new NodeCandidate(publicUrl)
                    {
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

                var current = networkManager.GetHosts().Select(x => new NodeCandidate(x.Url)
                {
                    ClientId = x.ClientId,
                    ConnectedPeers = x.NodeInfo?.ConnectedPeers ?? 0
                }).ToList();

                current.AddRange(nodes);

                var randomized = nodes.Select(x => x.Url).Distinct().OrderBy(arg => Guid.NewGuid());

                var connected = 0;
                
                foreach (var url in randomized)
                {
                    Console.WriteLine($"Connecting to {url}");
                    if(!await meshNetwork.AddNode(url, Guid.Empty))
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

        var peers = configuration.GetSection("Peers").Get<List<string>>() ?? new List<string>();

        if (peers.Count == 0)
        {
            logger.LogInformation("Resolving peers from testnet.kryolite.io");

            var result = await LookupClient.QueryAsync("testnet.kryolite.io", QueryType.TXT);
            
            if (result.HasError)
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }

            foreach (var txtRecord in result.Answers.TxtRecords().SelectMany(x => x.Text))
            {
                logger.LogInformation($"Peer: {txtRecord}");

                var uriBuilder = new UriBuilder(txtRecord);
                peers.Add(uriBuilder.Uri.ToString().TrimEnd('/'));
            }
        }

        foreach (var peer in peers)
        {
            if (!Uri.TryCreate(peer, new UriCreationOptions(), out var peerUri))
            {
                logger.LogWarning("Invalid uri format {}", peer);
                continue;
            }

            if (await meshNetwork.AddNode(peerUri, Guid.Empty))
            {
                var msg = new Message {
                    Payload = new NodeDiscovery()
                };

                await meshNetwork.BroadcastAsync(msg);
            }
        };

        if (peers.Count == 0)
        {
            logger.LogInformation("No peers resolved. Manually add peers in configuration.");
        }

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

public class ChainObserver : IObserver<Peer>
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
        logger.LogError(error, "Chain sync failed");
    }

    public async void OnNext(Peer node)
    {
        if (node.Blockchain == null || node.Blockchain.Count == 0) {
            return;
        }

        logger.LogInformation($"Starting chain sync (blocks={node.Blockchain.Count}) (chain from node {node.ClientId})");

        BeginSync?.Invoke(this, node.Blockchain.Count);

        var sortedBlocks = node.Blockchain.OrderBy(x => x.Height).ToList();

        var min = sortedBlocks.Min(x => x.Height);

        BigInteger totalWork = new BigInteger(0);

        var blockchainContext = new BlockchainExContext()
        {
            LastBlocks = blockchainManager.GetLastBlocks(min, 11)
                .OrderBy(x => x.Height)
                .ToList()
        };

        var blockExecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext)
            .Link<VerifyId>(x => x.Height > 0)
            .Link<VerifyParentHash>(x => x.Height > 0)
            .Link<VerifyNonce>();

        long progress = 0;
        ReportProgress(progress, sortedBlocks.Count);

        logger.LogInformation($"Verifying blocks");

        foreach (var block in sortedBlocks)
        {
            if (block.Pow is not null)
            {
                if(!blockExecutor.Execute(block.Pow, out var result)) 
                {
                    // TODO: disconnect from node and ban from peers
                    logger.LogError($"Chain failed at {block.Height} ({result})");
                    return;
                }

                totalWork += block.Pow.Difficulty.ToWork();
                blockchainContext.LastBlocks.Add(block.Pow);
            }

            ReportProgress(++progress, sortedBlocks.Count);
        }

        // Calculate how much work we would pop
        var height = blockchainManager.GetCurrentHeight();
        var toPop = blockchainManager.GetLastBlocks((int)(height - min));
        
        BigInteger toPopWork = new BigInteger();

        foreach (var block in toPop)
        {
            toPopWork += block.Difficulty.ToWork();
        }

        var localWork = blockchainManager.GetTotalWork();
        var remoteWork = localWork - toPopWork + totalWork;

        logger.LogInformation($"Current chain totalWork = {localWork}, received chain with totalWork = {remoteWork}");

        if (remoteWork > localWork)
        {
            logger.LogInformation("Chain is ahead, rolling forward");

            if(!blockchainManager.SetChain(sortedBlocks))
            {
                logger.LogWarning("Failed to set chain, discarding...");
                EndSync?.Invoke(this, EventArgs.Empty);
                return;
            }

            var msg = new Message
            {
                Payload = new QueryNodeInfo()
            };

            await node.SendAsync(msg);
        }

        logger.LogInformation($"Chain sync finished");

        EndSync?.Invoke(this, EventArgs.Empty);
    }
}
