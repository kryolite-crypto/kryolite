using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Kryolite.Node.Services;

public class SyncService : BackgroundService, IBufferService<Chain, SyncService>
{
    private const int BATCH_SIZE = 1000;

    private Channel<Chain> SyncChannel { get; } = Channel.CreateBounded<Chain>(3);
    private IServiceProvider ServiceProvider { get; }
    private ILogger<SyncService> Logger { get; }

    public SyncService(IServiceProvider serviceProvider, ILogger<SyncService> logger)
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
                Logger.LogError("null chain passed to SyncChannel");
                continue;
            }

            await HandleSynchronization(chain.Peer);
        }
    }

    public void Add(Chain item)
    {
        SyncChannel.Writer.TryWrite(item);
    }

    public void Add(List<Chain> items)
    {
        foreach (var item in items)
        {
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

    private async Task<bool> HandleSynchronization(Peer peer, bool findCommonHeight = true)
    {
        peer.IsSyncInProgress = true;

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            Logger.LogInformation("Initalizing staging context");

            ChainState? newState = null;
            IStateCache? stateCache = null;
            List<EventBase>? events = null;

            using (var checkpoint = storeManager.CreateCheckpoint())
            using (var staging = StagingManager.Open("staging", configuration, loggerFactory))
            {
                var commonHeight = 0L;

                if (findCommonHeight)
                {
                    commonHeight = await FindCommonHeight(staging, peer);
                }

                var stagingHeight = staging.GetChainState()?.Id;

                if (stagingHeight > commonHeight)
                {
                    staging.RollbackTo(commonHeight);
                }

                Logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Id}");

                Logger.LogInformation("Downloading and applying remote chain to staging context (this might take a while)");

                var i = commonHeight + 1;

                while(true)
                {
                    if (peer.SupportsRangeDownload)
                    {
                        if(await DownloadViewRange(peer, i, staging))
                        {
                            break;
                        }

                        i += BATCH_SIZE;
                    }
                    else
                    {
                        if(await DownloadView(peer, i, staging))
                        {
                            break;
                        }

                        i++;
                    }
                }

                newState = staging.GetChainState();

                if (newState is null)
                {
                    Logger.LogInformation("Failed to load chain in staging (chainstate not found)");
                    return false;
                }

                stateCache = staging.StateCache;
                events = staging.Events;
            }

            var chainState = storeManager.GetChainState();
            Logger.LogInformation($"Staging has height {newState.Id} and weight {newState.Weight}. Compared to local height {chainState.Id} and weight {chainState.Weight}");

            storeManager.LoadStagingChain("staging", newState, stateCache, events);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogInformation(ex, "ChainSync resulted in error");
            return false;
        }
        finally
        {
            peer.IsSyncInProgress = false;
        }
    }

    private async Task<long> FindCommonHeight(StagingManager stagingManager, Peer peer)
    {
        var height = stagingManager.GetChainState()?.Id ?? 0;

        var queryHeights = new List<long>
        {
            height,
            height - 1,
            height - 5,
            height - 25,
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
            var view = stagingManager.GetView(Math.Max(qHeight, 0));

            if (view is null)
            {
                continue;
            }

            query.Views.Add(view.GetHash());

            if (qHeight <= 0)
            {
                break;
            }
        }

        var response = await peer.PostAsync(query);

        if (response is null || response.Payload is not HeightResponse heightResponse)
        {
            Logger.LogInformation($"Failed to request common height from {peer.Uri.ToHostname()}");
            return 0;
        }

        Logger.LogInformation($"Found common height at {heightResponse.CommonHeight}");

        return heightResponse.CommonHeight;
    }

    private async Task<bool> DownloadViewRange(Peer peer, long id, StagingManager staging)
    {
        var result = await peer.PostAsync(new ViewRequestByRange(id, BATCH_SIZE));

        if (result is null || result.Payload is not ViewRangeResponse responses)
        {
            return true;
        }

        var pResult = Parallel.ForEach(responses.Views.SelectMany(x => x.Blocks), (block, state) =>
        {
            if (!block.VerifyNonce())
            {
                state.Stop();
            }
        });

        if (!pResult.IsCompleted)
        {
            return true;
        }

        pResult = Parallel.ForEach(responses.Views.SelectMany(x => x.Votes), (vote, state) =>
        {
            if (!vote.Verify())
            {
                state.Stop();
            }
        });

        if (!pResult.IsCompleted)
        {
            return true;
        }

        foreach (var response in responses.Views)
        {
            if (!staging.LoadBlocks(response.Blocks))
            {
                return true;
            }
            
            if (!staging.LoadVotes(response.Votes))
            {
                return true;
            }

            if (!staging.LoadTransactions(response.Transactions))
            {
                return true;
            }

            if (!staging.LoadView(response.View!))
            {
                return true;
            }
        }

        // if count is less than batch size we found the tip of chain!
        return responses.Views.Count < BATCH_SIZE;
    }

    private async Task<bool> DownloadView(Peer peer, long id, StagingManager staging)
    {
        var result = await peer.PostAsync(new ViewRequestById(id, false, true));

        if (result is null || result.Payload is not ViewResponse response || response.View is null)
        {
            Logger.LogDebug($"Found tip of chain at height {id - 1}");
            return true;
        }

        if (response.Blocks is null)
        {
            response.Blocks = new();
            await DownloadBlocks(peer, response);
        }

        if (response.Votes is null)
        {
            response.Votes = new();
            await DownloadVotes(peer, response);
        }
        
        if (response.Transactions is null)
        {
            response.Transactions = new();
            await DownloadTransactions(peer, response);
        }
        
        if (!staging.LoadBlocks(response.Blocks))
        {
            return true;
        }
        
        if (!staging.LoadVotes(response.Votes))
        {
            return true;
        }

        if (!staging.LoadTransactions(response.Transactions))
        {
            return true;
        }

        if (!staging.LoadView(response.View))
        {
            return true;
        }

        return false;
    }

    public async Task DownloadBlocks(Peer peer, ViewResponse response)
    {
        foreach (var blockhash in response.View!.Blocks)
        {
            var download = await peer.PostAsync(new BlockRequest(blockhash));

            if (download is null || download.Payload is not BlockResponse blockResponse || blockResponse.Block is null)
            {
                throw new Exception($"Block download from {peer.Uri.ToHostname()} failed at height {response.View.Id}");
            }

            response.Blocks.Add(blockResponse.Block);
        }
    }

    public async Task DownloadVotes(Peer peer, ViewResponse response)
    {
        foreach (var votehash in response.View!.Votes)
        {
            var download = await peer.PostAsync(new VoteRequest(votehash));

            if (download is null || download.Payload is not VoteResponse voteResponse || voteResponse.Vote is null)
            {
                throw new Exception($"Vote download from {peer.Uri.ToHostname()} failed at height {response.View.Id}");
            }

            response.Votes.Add(voteResponse.Vote);
        }
    }

    public async Task DownloadTransactions(Peer peer, ViewResponse response)
    {
        foreach (var txid in response.View!.Transactions)
        {
            var download = await peer.PostAsync(new TransactionRequest(txid));

            if (download is null || download.Payload is not TransactionResponse txResponse || txResponse.Transaction is null)
            {
                throw new Exception($"Transaction download from {peer.Uri.ToHostname()} failed at height {response.View.Id}");
            }

            response.Transactions.Add(txResponse.Transaction);
        }
    }
}
