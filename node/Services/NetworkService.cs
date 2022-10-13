using System.Numerics;
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
    private readonly INetworkManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly IMempoolManager mempoolManager;
    private readonly ILogger<NetworkService> logger;
    private readonly BufferBlock<Node> SyncBuffer = new BufferBlock<Node>();

    public NetworkService(IConfiguration configuration, ILogger<NetworkService> logger, INetworkManager networkManager, IBlockchainManager blockchainManager, IMempoolManager mempoolManager)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));

        NodeNetwork = new Network(configuration.GetValue<string>("NodeIp"), configuration.GetValue<int>("NodePort"), false);
    }

    private Network NodeNetwork;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SyncBuffer.AsObservable().Subscribe(new ChainObserver(NodeNetwork, blockchainManager));

        NodeNetwork.ClientDropped += async (object? sender, EventArgs args) => {
            if (sender is not Node node) {
                return;
            }

            // try to reconnect by re-adding same node
            if(!await NodeNetwork.AddNode(node.Hostname, node.Port, false)) {
                // TODO: get new host
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
                    logger.LogInformation("Received NodeInfo");

                    var nodeHost = new NodeHost
                    {
                        NodeInfo = nodeInfo
                    };

                    networkManager.AddHost(nodeHost);

                    Random rnd = new Random();

                    foreach (var peer in nodeInfo.Peers.OrderBy(x => rnd.Next()))
                    {
                        if(await NodeNetwork.AddNode(peer, false))
                        {
                            logger.LogInformation($"Discovered node {peer} from {args.Message.NodeId}");
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

                    var response = new Message
                    {
                        Payload = new NodeInfo
                        {
                            Height = chainState.Height,
                            TotalWork = chainState.TotalWork,
                            LastHash = blockchainManager.GetLastBlockhash(),
                            CurrentTime = DateTime.UtcNow,
                            Peers = NodeNetwork.GetPeers()
                        }
                    };

                    await node.SendAsync(response);
                    await NodeNetwork.AddNode(args.Hostname, queryNodeInfo.Port, false);

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
            await NodeNetwork.AddNode(uri.Host, uri.Port, false);
        });

        blockchainManager.OnBlockAdded(new ActionBlock<Block>(async block => {
            var msg = new Message
            {
                Payload = block
            };

            await NodeNetwork.BroadcastAsync(msg);
        }));

        mempoolManager.OnTransactionAdded(new ActionBlock<List<Transaction>>(async txs => {
            var msg = new Message
            {
                Payload = new TransactionData
                {
                    Transactions = txs
                }
            };

            await NodeNetwork.BroadcastAsync(msg);
        }));

        logger.LogInformation("Network \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
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
    public List<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public class ChainObserver : IObserver<Node>
{
    private readonly Network nodeNetwork;
    private readonly IBlockchainManager blockchainManager;

    public ChainObserver(Network nodeNetwork, IBlockchainManager blockchainManager)
    {
        this.nodeNetwork = nodeNetwork ?? throw new ArgumentNullException(nameof(nodeNetwork));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
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
        }

        if (totalWork > blockchainManager.GetTotalWork()) {
            blockchainManager.SetChain(blockchainContext.LastBlocks);

            var msg = new Message
            {
                Payload = new QueryNodeInfo
                {
                    Port = nodeNetwork.Port
                }
            };

            await node.SendAsync(msg);
        }
    }
}
