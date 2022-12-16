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
    private readonly IConfiguration configuration;
    private readonly StartupSequence startup;
    private readonly INetworkManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly IMempoolManager mempoolManager;
    private readonly ILogger<NetworkService> logger;
    private readonly BufferBlock<BaseNode> SyncBuffer = new BufferBlock<BaseNode>();

    public NetworkService(IConfiguration configuration, StartupSequence startup, ILogger<NetworkService> logger, INetworkManager networkManager, IBlockchainManager blockchainManager, IMempoolManager mempoolManager)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
    }

    private Network NodeNetwork;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() => startup.Blockchain.WaitOne());
        await Task.Run(() => startup.Mempool.WaitOne());

        logger.LogInformation("Starting Websocket server");
        NodeNetwork = new Network(configuration.GetValue<string>("NodeIp"), configuration.GetValue<int>("NodePort"), false);
        logger.LogInformation("Websocket server started");

        SyncBuffer.AsObservable().Subscribe(new ChainObserver(NodeNetwork, blockchainManager, logger));

        NodeNetwork.ClientDropped += async (object? sender, EventArgs args) => {
            if (sender is not BaseNode node) {
                return;
            }

            for (int i = 1; i <= 10; i++)
            {
                logger.LogInformation($"{i}/{10}: Connecting to {node.Hostname}:{node.Port}");
                if(await NodeNetwork.AddNode(node.Hostname, node.Port, false, node.ClientId)) 
                {
                    break;
                }
            }
        };

        NodeNetwork.MessageReceived += async (object? sender, MessageEventArgs args) =>
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

                    Random rnd = new Random();

                    foreach (var peer in nodeInfo.Peers.OrderBy(x => rnd.Next()))
                    {
                        if(await NodeNetwork.AddNode(peer.Key, false, peer.Value))
                        {
                            logger.LogInformation($"Discovered node {peer.Key} from {args.Message.NodeId}");
                        }
                    }

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

                    if (!Enumerable.SequenceEqual((byte[])nodeInfo.LastHash, (byte[])blockchainManager.GetLastBlockhash())) {
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
                    logger.LogInformation($"Received block {newBlock.Id} from {args.Message.NodeId}");
                    var height = blockchainManager.GetCurrentHeight();

                    if (height > newBlock.Id)
                    {
                        return;
                    }

                    if (height < (newBlock.Id - 1))
                    {
                        var msg2 = new Message
                        {
                            Payload = new RequestChainSync
                            {
                                StartBlock = blockchainManager.GetCurrentHeight(),
                                StartHash = blockchainManager.GetLastBlockhash()
                            }
                        };

                        await node.SendAsync(msg2);
                    }

                    if (blockchainManager.AddBlock(newBlock, false)) 
                    {
                        args.Rebroadcast = true;
                    }

                    break;
                case QueryNodeInfo queryNodeInfo:
                    logger.LogInformation($"Node query received from {args.Message.NodeId}");

                    var chainState = blockchainManager.GetChainState();
                    var hostname = $"http://{node.Hostname}:{queryNodeInfo.Port}";

                    var response = new Message
                    {
                        Payload = new NodeInfo
                        {
                            Height = chainState.POS.Height,
                            TotalWork = chainState.POW.TotalWork,
                            LastHash = blockchainManager.GetLastBlockhash(),
                            CurrentTime = DateTime.UtcNow,
                            Peers = NodeNetwork.GetPeers()
                                .Where(x => x.Key != hostname)
                                .ToDictionary(x => x.Key, x => x.Value)
                        }
                    };

                    await node.SendAsync(response);
                    await NodeNetwork.AddNode(args.Hostname, queryNodeInfo.Port, false, Guid.Empty);

                    break;
                case RequestChainSync syncParams:
                    logger.LogInformation($"Chain sync requested from {args.Message.NodeId}");

                    var block = blockchainManager.GetBlock(syncParams.StartBlock);

                    var chain = new Blockchain();

                    if (block == null) {
                        chain.Blocks = blockchainManager.GetFrom(0);
                        goto answer;
                    }

                    if (!Enumerable.SequenceEqual(block.GetHash().Buffer, syncParams.StartHash!)) {
                        chain.Blocks = blockchainManager.GetFrom(0);
                        goto answer;
                    }

                    if (syncParams.StartBlock == blockchainManager.GetCurrentHeight()) {
                        return;
                    }

                    chain.Blocks = blockchainManager.GetFrom(syncParams.StartBlock);
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

                    if (valid.Count > 0) {
                        args.Rebroadcast = true;
                    }
                    break;
                case Vote vote:
                    logger.LogInformation($"Received vote from {args.Hostname}");

                    if(blockchainManager.AddVote(vote))
                    {
                        args.Rebroadcast = true;
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

            await NodeNetwork.BroadcastAsync(msg);
        }));

        networkManager.OnBlockProposed(new ActionBlock<PowBlock>(block => {
            ProposeBlock(block);
        }));

        var transactionBuffer = new BufferBlock<Transaction>();

        transactionBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(100), Constant.MAX_BLOCK_TX)
            .Subscribe(async transactions => {
                var msg = new Message
                {
                    Payload = new TransactionData
                    {
                        Transactions = transactions
                    }
                };

                await NodeNetwork.BroadcastAsync(msg);
            });

        mempoolManager.OnTransactionAdded(transactionBuffer);

        logger.LogInformation("Reading peers from appsettings.json");
        var peers = configuration.GetSection("Peers").Get<string[]>() ?? new string[0];

        await Parallel.ForEachAsync(peers, async (peer, token) => {
            var uri = new Uri(peer);

            logger.LogInformation($"Connecting to {uri}");
            await NodeNetwork.AddNode(uri.Host, uri.Port, false, Guid.Empty);
        });

        logger.LogInformation("Network \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        startup.Network.Set();
    }

    public bool ProposeBlock(PowBlock block)
    {
        logger.LogInformation($"Proposing block {block.Id} to network...");
        // TODO: lock

        // TODO: Validate

        var chainState = blockchainManager.GetChainState();

        var posBlock = new PosBlock
        {
            Height = chainState.POS.Height + 1,
            ParentHash = chainState.POS.LastHash,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Pow = block
        };

        return blockchainManager.AddBlock(posBlock);
    }
}

[MessagePackObject]
public class Blockchain
{
    [Key(0)]
    public List<PowBlock>? Blocks { get; set; }
}

[MessagePackObject]
public class TransactionData
{
    [Key(0)]
    public IList<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public class ChainObserver : IObserver<BaseNode>
{
    private readonly Network nodeNetwork;
    private readonly IBlockchainManager blockchainManager;
    private readonly ILogger<NetworkService> logger;

    // TODO: quick hack, create proper events
    public static event EventHandler<long>? BeginSync;
    public static event EventHandler<double>? SyncProgress;
    public static event EventHandler<EventArgs>? EndSync;

    public ChainObserver(Network nodeNetwork, IBlockchainManager blockchainManager, ILogger<NetworkService> logger)
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

        var sortedBlocks = node.Blockchain.OrderBy(x => x.Id).ToList();

        var min = sortedBlocks.Min(x => x.Id);

        BigInteger totalWork = new BigInteger(0);

        var blockchainContext = new BlockchainContext()
        {
            LastBlocks = blockchainManager.GetLastBlocks(min, 11)
                .OrderBy(x => x.Id)
                .ToList()
        };

        var blockExecutor = Executor.Create<PowBlock, BlockchainContext>(blockchainContext)
            .Link<VerifyNonce>()
            .Link<VerifyId>(x => x.Id > 0)
            .Link<VerifyParentHash>(x => x.Id > 0);

        long progress = 0;
        ReportProgress(progress, sortedBlocks.Count);

        foreach (var block in sortedBlocks)
        {
            if(!blockExecutor.Execute(block, out var result)) {
                // TODO: disconnect from node and remove from peers
                // TODO: Replace with logger
                logger.LogError($"Chain failed at {block.Id} ({result})");
                return;
            }

            totalWork += block.Difficulty.ToWork();

            blockchainContext.LastBlocks.Add(block);

            ReportProgress(++progress, sortedBlocks.Count);
        }

        if (totalWork > blockchainManager.GetTotalWork()) {
            if(!blockchainManager.SetChain(blockchainContext.LastBlocks)) {
                EndSync?.Invoke(this, EventArgs.Empty);
                return;
            }

            var msg = new Message
            {
                Payload = new QueryNodeInfo
                {
                    Port = nodeNetwork.Port
                }
            };

            await node.SendAsync(msg);
        }

        EndSync?.Invoke(this, EventArgs.Empty);
    }
}
