using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Kryolite.Node.Services;

public class SyncService : BackgroundService, IBufferService<Chain, SyncService>
{
    private Channel<Chain> SyncChannel { get; } = Channel.CreateBounded<Chain>(3);
    private IServiceProvider ServiceProvider { get; }
    private ILogger<OutgoingTransactionService> Logger { get; }

    public SyncService(IServiceProvider serviceProvider, ILogger<OutgoingTransactionService> logger)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await SyncChannel.Reader.WaitToReadAsync(stoppingToken);

            if (!result)
            {
                Logger.LogError("SyncBuffer closed unexpectadly");
                return;
            }

            var chain = await SyncChannel.Reader.ReadAsync(stoppingToken);

            if (chain is null)
            {
                Logger.LogDebug("null chain passed to SyncChannel");
                continue;
            }

            await HandleSynchronization(chain.Peer, chain.Height);
        }
    }

    public void Add(Chain item)
    {
        item.Peer.IsSyncInProgress = true;
        SyncChannel.Writer.TryWrite(item);
    }

    public void Add(List<Chain> items)
    {
        foreach (var item in items)
        {
            item.Peer.IsSyncInProgress = true;
            SyncChannel.Writer.TryWrite(item);
        }
    }

    public Task AddAsync(Chain item)
    {
        throw new NotImplementedException();
    }

    public Task AddAsync(List<Chain> items)
    {
        throw new NotImplementedException();
    }

    private async Task HandleSynchronization(Peer peer, long height)
    {
        peer.IsSyncInProgress = true;

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            var queryHeights = new List<long>
            {
                height - 1,
                height - 5,
                height - 10,
                height - 50,
                height - 100,
                height - 500,
                height - 1000,
                height - 5000,
                height - 10000,
                height - 50000,
                height - 100000,
                height - 500000,
                height - 1000000,
            };

            var query = new HeightRequest();

            foreach (var qHeight in queryHeights)
            {
                var state = storeManager.GetChainStateAt(Math.Max(qHeight, 0));

                if (state is null)
                {
                    continue;
                }

                query.Views.Add(state.LastHash);

                if (qHeight <= 0)
                {
                    break;
                }
            }

            var response = await peer.PostAsync(query);

            if (response is null || response.Payload is not HeightResponse heightResponse)
            {
                Logger.LogInformation($"Failed to request common height from {peer.Uri.ToHostname()}");
                return;
            }

            Logger.LogInformation("Initalizing staging context");

            ChainState? newState = null;
            IStateCache? stateCache = null;
            List<EventBase>? events = null;

            using (var staging = StagingManager.Create("staging", configuration, loggerFactory))
            {
                Logger.LogInformation($"Loading local transactions up to height {heightResponse.CommonHeight}");

                for (var i = 1; i <= heightResponse.CommonHeight; i++)
                {
                    var txs = storeManager.GetTransactionsAtHeight(i);

                    if (txs.Count == 0)
                    {
                        break;
                    }

                    staging.DisableLogging();
                    var success = staging.LoadTransactionsWithoutValidation(txs);
                    staging.EnableLogging();

                    if (!success)
                    {
                        Logger.LogError($"Failed to setup staging from current db");
                        return;
                    }
                }

                Logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Height}");
                Logger.LogInformation("Downloading and applying remote chain to staging context (this might take a while)");

                const int BATCH_SIZE = 100;

                for (var i = heightResponse.CommonHeight + 1; i <= height; i += BATCH_SIZE)
                {
                    Logger.LogDebug($"Downloading transactions from {i} to {i + BATCH_SIZE}");

                    var request = new DownloadRequest(i, i + BATCH_SIZE);
                    var txResponse = await peer.PostAsync(request);

                    if (txResponse is null || txResponse.Payload is not DownloadResponse download)
                    {
                        Logger.LogInformation($"Chain download from {peer.Uri.ToHostname()} failed at height {i} - {i + BATCH_SIZE}");
                        break;
                    }

                    Logger.LogDebug($"Loading {download.Transactions.Count} to staging");

                    if (!staging.LoadTransactions(download.Transactions))
                    {
                        Logger.LogInformation($"Applying remote chain in staging failed");

                        if (networkManager.Ban(peer.ClientId))
                        {
                            await peer.DisconnectAsync();
                        }
                        else
                        {
                            // Force sync from beginning
                            await AddAsync(new Chain(peer, 0));
                        }

                        break;
                    }
                }

                newState = staging.GetChainState();

                if (newState is null)
                {
                    Logger.LogInformation("Failed to load chain in staging (chainstate not found)");
                    return;
                }

                stateCache = staging.StateCache;
                events = staging.Events;
            }

            var chainState = storeManager.GetChainState();
            Logger.LogInformation($"Staging has height {newState.Height} and weight {newState.Weight}. Compared to local height {chainState.Height} and weight {chainState.Weight}");

            storeManager.LoadStagingChain("staging", newState, stateCache, events);

            await peer.SendAsync(new NodeInfoRequest());
        }
        catch (Exception ex)
        {
            Logger.LogInformation(ex, "ChainSync resulted in error");
        }
        finally
        {
            peer.IsSyncInProgress = false;
        }
    }
}
