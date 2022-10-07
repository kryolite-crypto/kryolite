using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using MessagePack;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Marccacoin.NetworkManager;

namespace Marccacoin;

public class NetworkService : BackgroundService
{
    private readonly IConfiguration configuration;
    private readonly IDiscoveryManager networkManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly BufferBlock<Node> SyncBuffer = new BufferBlock<Node>();

    public NetworkService(IConfiguration configuration, ILogger<NetworkService> logger, IDiscoveryManager networkManager, IBlockchainManager blockchainManager)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));

        NodeNetwork = new Network(configuration.GetValue<string>("NodeIp"), configuration.GetValue<int>("NodePort"), false);
    }

    private ILogger<NetworkService> Logger { get; }
    private Network NodeNetwork;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SyncBuffer.AsObservable().Subscribe(new ChainObserver(blockchainManager));

        NodeNetwork.MessageReceived += async (object? sender, MessageEventArgs args) =>
        {
            if (args.Message.Payload is null) 
            {
                Logger.LogError("Empty payload received");
                return;
            }

            switch (args.Message.Payload) 
            {
                case Query query:
                    await HandleQuery(query, args.Message.NodeId);
                    break;
                case NodeInfo nodeInfo:
                    Logger.LogInformation("Received NodeInfo");

                    if (sender is not Node node) {
                        Logger.LogError("NodeInfo received from unknown source");
                        return;
                    }

                    var nodeHost = new NodeHost
                    {
                        NodeInfo = nodeInfo
                    };

                    networkManager.AddHost(nodeHost);

                    if (nodeInfo.TotalWork > blockchainManager.GetTotalWork())
                    {
                        var msg = new Message
                        {
                            Payload = new Query {
                                QueryType = QueryType.CHAIN_SYNC,
                                Params = new ChainSyncParams
                                {
                                    StartBlock = blockchainManager.GetCurrentHeight(),
                                    StartHash = blockchainManager.GetLastBlockhash()
                                }
                            }
                        };

                        await node.SendAsync(msg);
                    }
                    break;
                case Blockchain blockchain:
                    if (sender is not Node node2) {
                        Logger.LogError("Blockchain received from unknown source");
                        return;
                    }

                    Logger.LogInformation("Received blockchain");
                    if (blockchain.Blocks != null) {
                        node2.Blockchain = blockchain.Blocks;
                        SyncBuffer.Post<Node>(node2);
                    }
                    break;
                case Block block:
                    Logger.LogInformation($"Received block {block.Id} from {args.Message.NodeId}");

                    if (blockchainManager.AddBlock(block)) {
                        args.Rebroadcast = true;
                    }

                    break;
                default:
                    Logger.LogError($"Invalid payload type: {args.Message.Payload.GetType()}");
                    break;
            }
        };

        var peers = configuration.GetSection("Peers").Get<string[]>() ?? new string[0];

        await Parallel.ForEachAsync(peers, async (peer, token) => {
            Logger.LogInformation($"Connecting to {peer}");
            var node = NodeNetwork.AddNode("127.0.0.1", 6000, false);

            var msg = new Message
            {
                Payload = new Query 
                {
                    QueryType = QueryType.NODE_INFO
                }
            };

            await node.SendAsync(msg);
        });

        blockchainManager.OnBlockAdded(new ActionBlock<Block>(async block => {
            var msg = new Message
            {
                Payload = block
            };

            await NodeNetwork.BroadcastAsync(msg);
        }));

        Logger.LogInformation("Network \t\x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");
    }

    private async Task HandleQuery(Query query, Guid? senderGuid)
    {
        Logger.LogInformation($"Received Query: {query.QueryType}");

        if (senderGuid == null) 
        {
            Logger.LogError($"Invalid senderid {senderGuid}");
            return;
        }

        switch (query.QueryType)
        {
            case QueryType.NODE_INFO:
                var msg = new Message
                {
                    Payload = new NodeInfo
                    {
                        Height = blockchainManager.GetCurrentHeight(),
                        TotalWork = blockchainManager.GetTotalWork(),
                        CurrentTime = DateTime.UtcNow
                    }
                };

                await NodeNetwork.SendAsync(senderGuid.Value, msg);
                break;
            case QueryType.CHAIN_SYNC:
                if (query.Params is not ChainSyncParams syncParams) {
                    Logger.LogError($"Invalid chain sync params");
                    return;
                }

                var block = blockchainManager.GetBlock(syncParams.StartBlock);

                var chain = new Blockchain();

                if (block == null) {
                    // send full chain
                    Logger.LogInformation("Send full chain");
                    chain.Blocks = blockchainManager.GetFrom(0);
                    goto answer;
                }

                if (!Enumerable.SequenceEqual(block.GetHash().Buffer, syncParams.StartHash!)) {
                    // send full chain
                    Logger.LogInformation("Send full chain");
                    chain.Blocks = blockchainManager.GetFrom(0);
                    goto answer;
                }

                if (syncParams.StartBlock == blockchainManager.GetCurrentHeight()) {
                    // Chains equals, do nothing
                    return;
                }

                Logger.LogInformation("Send partial chain");
                chain.Blocks = blockchainManager.GetFrom(syncParams.StartBlock);
answer:
                var answer = new Message()
                {
                    Payload = chain
                };

                await NodeNetwork.SendAsync(senderGuid.Value, answer);
                break;
            default:
                Logger.LogError($"Invalid query received: {query}");
                break;
        }
    }
}

[MessagePackObject]
public class Blockchain
{
    [Key(0)]
    public List<Block>? Blocks { get; set; }
}

public class ChainObserver : IObserver<Node>
{
    private readonly IBlockchainManager blockchainManager;

    public ChainObserver(IBlockchainManager blockchainManager)
    {
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
    }
    
    public void OnCompleted()
    {
        
    }

    public void OnError(Exception error)
    {
        throw new Exception("ChainObserver failed", error);
    }

    public void OnNext(Node node)
    {
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
                Payload = new Query 
                {
                    QueryType = QueryType.NODE_INFO
                }
            };

            node.SendAsync(msg).RunSynchronously();
        }
    }
}
