using System.Numerics;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Marccacoin.NetworkManager;

namespace Marccacoin;

public class NetworkService : BackgroundService
{
    private readonly IConfiguration configuration;
    private readonly StartupSequence startup;
    private readonly INetworkManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly IMempoolManager mempoolManager;
    private readonly ILogger<NetworkService> logger;
    private readonly BufferBlock<Node> SyncBuffer = new BufferBlock<Node>();

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

        NodeNetwork = new Network(configuration.GetValue<string>("NodeIp"), configuration.GetValue<int>("NodePort"), false);
        SyncBuffer.AsObservable().Subscribe(new ChainObserver(NodeNetwork, blockchainManager));

        NodeNetwork.ClientDropped += async (object? sender, EventArgs args) => {
            if (sender is not Node node) {
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

            if (sender is not Node node) 
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
                        SyncBuffer.Post<Node>(node);
                    }

                    break;
                case Block newBlock:
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
                    var chainState = blockchainManager.GetChainState();
                    var hostname = $"http://{node.Hostname}:{queryNodeInfo.Port}";

                    var response = new Message
                    {
                        Payload = new NodeInfo
                        {
                            Height = chainState.Height,
                            TotalWork = chainState.TotalWork,
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
                default:
                    logger.LogError($"Invalid payload type: {args.Message.Payload.GetType()}");
                    break;
            }
        };

        var peers = configuration.GetSection("Peers").Get<string[]>() ?? new string[0];

        await Parallel.ForEachAsync(peers, async (peer, token) => {
            var uri = new Uri(peer);

            logger.LogInformation($"Connecting to {uri}");
            await NodeNetwork.AddNode(uri.Host, uri.Port, false, Guid.Empty);
        });

        blockchainManager.OnBlockAdded(new ActionBlock<Block>(async block => {
            var msg = new Message
            {
                Payload = block
            };

            await NodeNetwork.BroadcastAsync(msg);
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

        logger.LogInformation("Network \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
        startup.Network.Set();
    }
}

[MessagePackObject]
public class Blockchain
{
    [Key(0)]
    public List<Block>? Blocks { get; set; }
}

[MessagePackObject]
public class TransactionData
{
    [Key(0)]
    public IList<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public class ChainObserver : IObserver<Node>
{
    private readonly Network nodeNetwork;
    private readonly IBlockchainManager blockchainManager;

    // TODO: quick hack, create proper events
    public static event EventHandler<long>? BeginSync;
    public static event EventHandler<double>? SyncProgress;
    public static event EventHandler<EventArgs>? EndSync;

    public ChainObserver(Network nodeNetwork, IBlockchainManager blockchainManager)
    {
        this.nodeNetwork = nodeNetwork ?? throw new ArgumentNullException(nameof(nodeNetwork));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
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

    public async void OnNext(Node node)
    {
        if (node.Blockchain == null || node.Blockchain.Count == 0) {
            return;
        }

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

        var blockExecutor = Executor.Create<Block, BlockchainContext>(blockchainContext)
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
                Console.WriteLine($"Chain failed at {block.Id} ({result})");
                return;
            }

            totalWork += block.Header.Difficulty.ToWork();

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
