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

            if (chain.Peer.SupportsReplyTo)
            {
                await HandleSynchronization(chain.Peer, chain.Height);
            }
            else
            {
                await HandleSynchronization(chain);
            }
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
                if (qHeight <= 0)
                {
                    continue;
                }

                var state = storeManager.GetChainStateAt(qHeight);

                if (state is null)
                {
                    continue;
                }

                query.Views.Add(state.LastHash);
            }

            var response = await peer.PostAsync(query);

            if (response is null || response.Payload is not HeightResponse heightResponse)
            {
                Logger.LogInformation($"Failed to request possible views from {peer.Uri.ToHostname()}");
                return;
            }

            Logger.LogInformation("Initalizing staging context");

            using var staging = StagingManager.Create("staging", configuration, loggerFactory);

            Logger.LogInformation($"Loading local transactions up to height {heightResponse.CommonHeight}");

            for (var i = 1; i < heightResponse.CommonHeight; i++)
            {
                var txs = storeManager.GetTransactionsAtHeight(i);

                if (txs.Count > 0)
                {
                    staging.DisableLogging();
                    var success = staging.LoadTransactionsWithoutValidation(txs);
                    staging.EnableLogging();

                    if (!success)
                    {
                        Logger.LogError($"Failed to setup staging from current db");
                        return;
                    }
                }
            }

            Logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Height}");
            Logger.LogInformation("Downloading and applying remote chain to staging context (this might take a while)");

            for (var i = heightResponse.CommonHeight; i <= height; i += 100)
            {
                var request = new DownloadRequest(i, i + 100);
                var txResponse = await peer.PostAsync(request);

                if (txResponse is null || txResponse.Payload is not DownloadResponse download)
                {
                    Logger.LogInformation($"Chain download from {peer.Uri.ToHostname()} failed at height {i} - {1 + 100}");
                    break;
                }

                if (!staging.LoadTransactions(download.Transactions))
                {
                    Logger.LogInformation($"Failed to apply chain from {peer.Uri.ToHostname()} at height {i} - {1 + 100}");
                    break;
                }
            }

            var newState = staging.GetChainState();

            if (newState is null)
            {
                Logger.LogInformation("Failed to load chain in staging (chainstate not found)");
                return;
            }

            var chainState = storeManager.GetChainState();
            Logger.LogInformation($"Staging has height {newState.Height} and weight {newState.Weight}. Compared to local height {chainState.Height} and weight {chainState.Weight}");

            var loaded = storeManager.LoadStagingChain("staging", newState, staging.StateCache, staging.Events);

            if (loaded)
            {
                await peer.SendAsync(new QueryNodeInfo());
            }
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

    private async Task HandleSynchronization(Chain chain)
    {
        chain.Peer.IsSyncInProgress = true;

        try
        {
            await Task.CompletedTask;

            using var scope = ServiceProvider.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

            var chainState = storeManager.GetChainState();

            var maxView = chain.Transactions
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .MaxBy(x => BitConverter.ToInt64(x.Data));

            if (maxView is null || maxView.CalculateHash() == chainState.LastHash)
            {
                storeManager.AddTransactionBatch(chain.Transactions, true);
                return;
            }

            var hasNewView = chain.Transactions
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .Where(x => !storeManager.Exists(x.CalculateHash()))
                .Any();

            if (!hasNewView)
            {
                storeManager.AddTransactionBatch(chain.Transactions, true);
                return;
            }

            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            Logger.LogInformation("Initalizing staging context");

            using var staging = StagingManager.Create("staging", configuration, loggerFactory);

            var minView = chain.Transactions
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .Where(x => !storeManager.Exists(x.CalculateHash()))
                .MinBy(x => BitConverter.ToInt64(x.Data));

            var minHeight = BitConverter.ToInt64(minView?.Data ?? new byte[8]);

            BlockchainService.InitializeGenesisBlock(staging, Logger);

            Logger.LogInformation($"Loading local transactions up to height {minHeight}");

            for (var i = 1; i < minHeight; i++)
            {
                var txs = storeManager.GetTransactionsAtHeight(i);

                if (txs.Count > 0)
                {
                    staging.DisableLogging();
                    var success = staging.LoadTransactionsWithoutValidation(txs);
                    staging.EnableLogging();

                    if (!success)
                    {
                        Logger.LogError($"Failed to setup staging from current db");
                        return;
                    }
                }
            }

            Logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Height}");
            Logger.LogInformation("Loading remote chain to staging context (this might take a while)");

            staging.LoadTransactions(chain.Transactions);

            Logger.LogInformation("Remote chain loaded in staging");

            var newState = staging.GetChainState();

            if (newState is null)
            {
                Logger.LogInformation("Failed to load chain in staging (chainstate not found)");
                return;
            }

            chainState = storeManager.GetChainState();
            Logger.LogInformation($"Staging has height {newState.Height} and weight {newState.Weight}. Compared to local height {chainState.Height} and weight {chainState.Weight}");

            var loaded = storeManager.LoadStagingChain("staging", newState, staging.StateCache, staging.Events);

            if (loaded)
            {
                await chain.Peer.SendAsync(new QueryNodeInfo());
            }
        }
        catch (Exception ex)
        {
            Logger.LogInformation(ex, "ChainSync resulted in error");
        }
        finally
        {
            chain.Peer.IsSyncInProgress = false;
        }
    }
}
