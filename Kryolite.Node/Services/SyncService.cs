using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Network;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Kryolite.Node.Services;


public class SyncManager : BackgroundService
{
    private const int BATCH_SIZE = 1000;

    private static Channel<Network.Node> _channel = Channel.CreateBounded<Network.Node>(3);
    private IServiceProvider _serviceProvider;
    private ILogger<SyncManager> _logger;

    public SyncManager(IServiceProvider serviceProvider, ILogger<SyncManager> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void AddToQueue(Network.Node node)
    {
        _channel.Writer.TryWrite(node);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncMan       [UP]");

        try
        {
            await foreach (var node in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                Synchronize(node);
            }
        }
        catch (OperationCanceledException)
        {
            // Do nothing, shutting down
        }

        _logger.LogInformation("SyncMan       [DOWN]");
    }

    private void Synchronize(Network.Node node)
    {
        node.IsSyncInProgress = true;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var connMan = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            _logger.LogInformation("Initalizing staging context");

            ChainState? newState = null;
            IStateCache? stateCache = null;
            List<EventBase>? events = null;

            var client = connMan.CreateClient<INodeService>(node);

            using (var checkpoint = storeManager.CreateCheckpoint())
            using (var staging = StagingManager.Open("staging", configuration, loggerFactory))
            {
                var commonHeight = FindCommonHeight(staging, client);
                var stagingHeight = staging.GetChainState()?.Id;

                if (stagingHeight > commonHeight)
                {
                    staging.RollbackTo(commonHeight);
                }

                _logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Id}");

                var i = commonHeight + 1;
                var brokenChain = false;

                _logger.LogInformation("Downloading and applying remote chain to staging context (this might take a while)");

                while(true)
                {
                    (bool completed, brokenChain) = DownloadViewRange(client, i, staging);

                    if(completed)
                    {
                        break;
                    }

                    i += BATCH_SIZE;

                }

                if (brokenChain)
                {
                    node.IsForked = true;
                }

                newState = staging.GetChainState();

                if (newState is null)
                {
                    _logger.LogInformation("Failed to load chain in staging (chainstate not found)");
                    return;
                }

                stateCache = staging.StateCache;
                events = staging.Events;
            }

            var chainState = storeManager.GetChainState();
            _logger.LogInformation($"Staging has height {newState.Id} and weight {newState.Weight}. Compared to local height {chainState.Id} and weight {chainState.Weight}");

            storeManager.LoadStagingChain("staging", newState, stateCache, events);

            return;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "ChainSync resulted in error");
        }
        finally
        {
            node.IsSyncInProgress = false;
        }
    }

    private long FindCommonHeight(StagingManager stagingManager, INodeService client)
    {
        var height = stagingManager.GetChainState()?.Id ?? 0;

        var queryHashes = new List<SHA256Hash>(15);

        for (var i = 14; i >= 0; i--)
        {
            var qHeight = Math.Min(1, i * 5);
            var view = stagingManager.GetView(Math.Max(height - qHeight, 1));

            if (view is null || qHeight <= 0)
            {
                break;
            }

            queryHashes.Add(view.GetHash());
        }

        var commonHeight = client.FindCommonHeight(queryHashes);

        _logger.LogInformation($"Found common height at {commonHeight}");

        return commonHeight;
    }

    private (bool Completed, bool BrokenChain) DownloadViewRange(INodeService client, long height, StagingManager staging)
    {
        var views = client.GetViewsForRange(height, BATCH_SIZE);

        var pResult = Parallel.ForEach(views.SelectMany(x => x.Blocks), (block, state) =>
        {
            if (!block.VerifyNonce())
            {
                state.Stop();
            }
        });

        if (!pResult.IsCompleted)
        {
            return (true, true);
        }

        pResult = Parallel.ForEach(views.SelectMany(x => x.Votes), (vote, state) =>
        {
            if (!vote.Verify())
            {
                state.Stop();
            }
        });

        if (!pResult.IsCompleted)
        {
            return (true, true);
        }

        // Transactions are verified later in LoadTransactions method

        foreach (var response in views)
        {
            if (!staging.LoadBlocks(response.Blocks))
            {
                return (true, true);
            }
            
            if (!staging.LoadVotes(response.Votes))
            {
                return (true, true);
            }

            if (!staging.LoadTransactions(response.Transactions))
            {
                return (true, true);
            }

            if (!staging.LoadView(response.View!))
            {
                return (true, true);
            }
        }

        // if count is less than batch size we found the tip of chain!
        return (views.Count < BATCH_SIZE, false);
    }
}
