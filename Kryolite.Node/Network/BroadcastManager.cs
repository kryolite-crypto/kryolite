using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class BroadcastManager : BackgroundService
{
    private readonly IConnectionManager _connMan;
    private readonly ILogger<BroadcastManager> _logger;
    private readonly PublicKey _nodeKey;

    private static TransformBlock<IBroadcast, byte[]> _transform { get; } = new(x => Serializer.Serialize<IBroadcast>(x));
    private static BatchUntilInactiveBlock<byte[]> _batch { get; } = new(100, TimeSpan.FromMilliseconds(50));
    private static BroadcastBlock<byte[][]> _broadcast { get; } = new(x => x);
    private static Channel<byte[][]> _channel = Channel.CreateUnbounded<byte[][]>();

    static BroadcastManager()
    {
        _transform.LinkTo(_batch);
        _batch.LinkTo(_broadcast);
    } 

    public BroadcastManager(IConnectionManager connMan, IKeyRepository keyRepo, ILogger<BroadcastManager> logger)
    {
        _connMan = connMan;
        _logger = logger;
        _nodeKey = keyRepo.GetPublicKey();
    }

    public static void Broadcast(IBroadcast packet)
    {
        _transform.Post(packet);
    }

    public static IDisposable Subscribe(ActionBlock<byte[][]> action)
    {
        return _broadcast.LinkTo(action);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BrdcstMan     [UP]");

        try
        {
            var action = new ActionBlock<byte[][]>(bytes => _channel.Writer.WriteAsync(bytes, stoppingToken));
            using var sub = Subscribe(action);

            // We use channel since we can asynchronously wait on it
            await foreach (var bytes in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                var batch = new BatchBroadcast(bytes);
                var data = Serializer.Serialize(batch);

                try
                {
                    Parallel.ForEach(_connMan.GetConnectedNodes(), connection =>
                    {
                        var client = _connMan.CreateClient(connection);
                        client.Broadcast(data);
                    });
                }
                catch (OperationCanceledException)
                {
                    // This gets thrown when client is disconnected
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Do nothing, we are shutting down
        }

        _logger.LogInformation("BrdcstMan     [DOWN]");
    }
}
