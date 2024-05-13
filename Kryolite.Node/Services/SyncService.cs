using Kryolite.EventBus;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Blockchain;
using Kryolite.Node.Network;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace Kryolite.Node.Services;


public class SyncManager : BackgroundService
{
    private const int BATCH_SIZE = 1000;

    private static Channel<NodeConnection> _channel = Channel.CreateBounded<NodeConnection>(3);
    private IServiceProvider _serviceProvider;
    private ILogger<SyncManager> _logger;

    public SyncManager(IServiceProvider serviceProvider, ILogger<SyncManager> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void AddToQueue(NodeConnection node)
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

    private void Synchronize(NodeConnection connection)
    {
        if (connection.Node.IsForked)
        {
            _logger.LogInformation("[{node}] Skipping synchronization due to forked chain", connection.Node.Uri.ToHostname());
            return;
        }

        connection.Node.IsSyncInProgress = true;
        var sw = Stopwatch.StartNew();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var connMan = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            ChainState? newState = null;
            IStateCache? stateCache = null;
            List<EventBase>? events = null;

            var client = connMan.CreateClient(connection);

            _logger.LogInformation("[{node}] Initializing staging context", connection.Node.Uri.ToHostname());

            using (var checkpoint = storeManager.CreateCheckpoint())
            using (var staging = StagingManager.Open("staging", configuration))
            {
                var commonHeight = FindCommonHeight(staging, connection.Node, client);
                var stagingHeight = staging.GetChainState()?.Id;

                if (stagingHeight > commonHeight)
                {
                    staging.RollbackTo(commonHeight);
                }

                _logger.LogInformation("[{node}] Staging context loaded to height {height}", connection.Node.Uri.ToHostname(), staging.GetChainState()?.Id);

                var i = commonHeight + 1;
                var brokenChain = false;

                _logger.LogInformation("[{node}] Downloading and applying remote chain to staging context (this might take a while)", connection.Node.Uri.ToHostname());

                while(true)
                {
                    _logger.LogInformation("[{node}] Downloading and applying batch {start} - {end}", connection.Node.Uri.ToHostname(), i, i + BATCH_SIZE);
                    (bool completed, brokenChain) = DownloadViewRange(client, i, staging);

                    if(completed)
                    {
                        break;
                    }

                    i += BATCH_SIZE;

                }

                if (brokenChain)
                {
                    _logger.LogInformation("[{node}] Failed to apply remote chain (broken chain received)", connection.Node.Uri.ToHostname());
                    connection.Node.IsForked = true;
                }

                newState = staging.GetChainState();

                if (newState is null)
                {
                    _logger.LogInformation("[{node}] Failed to load chain in staging (chainstate not found)", connection.Node.Uri.ToHostname());
                    return;
                }

                stateCache = staging.StateCache;
                events = staging.Events;
            }

            var chainState = storeManager.GetChainState();
            _logger.LogInformation("[{node}] Staging has height {id} and weight {weight}. Compared to local height {cId} and weight {cWeight}",
                connection.Node.Uri.ToHostname(),
                newState.Id,
                newState.Weight,
                chainState.Id,
                chainState.Weight
            );

            storeManager.LoadStagingChain("staging", newState, stateCache, events);

            return;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex.Message);
        }
        finally
        {
            connection.Node.IsSyncInProgress = false;
            sw.Stop();
            _logger.LogInformation("Synchronization completed in {elapsed:N2} seconds", sw.Elapsed.TotalSeconds);
        }
    }

    private long FindCommonHeight(StagingManager stagingManager, Network.Node node, INodeService client)
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

        var commonHeight = client.FindCommonHeight(new HashList(queryHashes));

        _logger.LogInformation("[{node}] Found common height at {commonHeight}", node.Uri.ToHostname(), commonHeight);

        return commonHeight;
    }

    private (bool Completed, bool BrokenChain) DownloadViewRange(INodeService client, long height, StagingManager staging)
    {
        var views = client.GetViewsForRange(new ViewListRequest(height, BATCH_SIZE));

        var pResult = Parallel.ForEach(views.Views.SelectMany(x => x.Blocks), (block, state) =>
        {
            if (!block.VerifyNonce())
            {
                state.Stop();
            }
        });

        if (!pResult.IsCompleted)
        {
            _logger.LogInformation("Failed to verify blocks");
            return (true, true);
        }

        pResult = Parallel.ForEach(views.Views.SelectMany(x => x.Votes), (vote, state) =>
        {
            if (!vote.Verify())
            {
                state.Stop();
            }
        });

        if (!pResult.IsCompleted)
        {
            _logger.LogInformation("Failed to verify votes");
            return (true, true);
        }

        // Transactions are verified later in LoadTransactions method

        foreach (var response in views.Views)
        {
            if (!staging.LoadBlocks(response.Blocks))
            {
                _logger.LogInformation("Failed to load blocks");
                return (true, true);
            }
            
            if (!staging.LoadVotes(response.Votes))
            {
                _logger.LogInformation("Failed to load votes");
                return (true, true);
            }

            if (!staging.LoadTransactions(response.Transactions))
            {
                _logger.LogInformation("Failed to load transactions");
                return (true, true);
            }

            if (!staging.LoadView(response.View!))
            {
                _logger.LogInformation("Failed to load view");
                return (true, true);
            }
        }

        // if count is less than batch size we found the tip of chain!
        return (views.Views.Count < BATCH_SIZE, false);
    }
}
