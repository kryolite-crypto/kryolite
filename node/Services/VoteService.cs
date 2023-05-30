using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Kryolite.Node.Services;

public class VoteService : BackgroundService, IBufferService<Vote>
{
    private Channel<Vote> VoteChannel { get; } = Channel.CreateUnbounded<Vote>();
    private IMeshNetwork MeshNetwork { get; }
    private ILogger<TransactionService> Logger { get; }

    public VoteService(IMeshNetwork meshNetwork, ILogger<TransactionService> logger)
    {
        MeshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await VoteChannel.Reader.WaitToReadAsync(stoppingToken);

            if (!result)
            {
                Logger.LogError("VoteBuffer closed unexpectadly");
                break;
            }

            // buffer transactions for few moments
            await Task.Delay(100);

            var items = VoteChannel.Reader.ReadAllAsync(stoppingToken)
                .ToBlockingEnumerable(stoppingToken);

            var msg = new VoteBatch
            {
                Votes = items.ToList()
            };

            await MeshNetwork.BroadcastAsync(msg);
        }
    }

    public void Add(Vote item)
    {
        VoteChannel.Writer.TryWrite(item);
    }

    public void Add(List<Vote> items)
    {
        foreach (var item in items)
        {
            VoteChannel.Writer.TryWrite(item);
        }
    }
}
