using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop.Infrastructure;
using System.Threading.Channels;

namespace Kryolite.Node.Services;

public class SyncService : BackgroundService, IBufferService<Chain, SyncService>
{
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

            var ok = await HandleSynchronization(chain.Peer, chain.Height);

            if (ok)
            {
                await chain.Peer.SendAsync(new NodeInfoRequest());
                continue;
            }

            ok = await HandleSynchronization(chain.Peer, chain.Height, false);

            if (ok)
            {
                await chain.Peer.SendAsync(new NodeInfoRequest());
                continue;
            }
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

    private async Task<bool> HandleSynchronization(Peer peer, long height, bool findCommonHeight = true)
    {
        peer.IsSyncInProgress = true;

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            var commonHeight = 0L;

            if (findCommonHeight)
            {
                commonHeight = await FindCommonHeight(storeManager, peer, height);
            }

            Logger.LogInformation("Initalizing staging context");

            ChainState? newState = null;
            IStateCache? stateCache = null;
            List<EventBase>? events = null;

            using (var staging = StagingManager.Create("staging", configuration, loggerFactory))
            {
                Logger.LogInformation($"Loading local transactions up to height {commonHeight}");

                staging.DisableLogging();

                for (var i = 1; i <= commonHeight; i++)
                {
                    var view = storeManager.GetView(i);

                    if (view is null)
                    {
                        break;
                    }

                    var txs = storeManager.GetTransactions(view.Transactions);
                    var blocks = storeManager.GetBlocks(view.Blocks);
                    var votes = storeManager.GetVotes(view.Votes);

                    if (!staging.LoadBlocksWithoutValidation(blocks))
                    {
                        Logger.LogError($"Failed to apply blocks to staging");
                        return false;
                    }

                    if (!staging.LoadVotesWithoutValidation(votes))
                    {
                        Logger.LogError($"Failed to apply votes to staging");
                        return false;
                    }

                    if (!staging.LoadTransactionsWithoutValidation(txs))
                    {
                        Logger.LogError($"Failed to apply transaction to staging");
                        return false;
                    }

                    if (!staging.LoadViewWithoutValidation(view))
                    {
                        Logger.LogError($"Failed to apply view to staging");
                        return false;
                    }
                }

                staging.EnableLogging();

                Logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Id}");
                Logger.LogInformation("Downloading and applying remote chain to staging context (this might take a while)");

                bool brokenChain = false;

                for (var i = commonHeight + 1; i <= height; i++)
                {
                    Logger.LogDebug($"Downloading view #{i}");

                    var result = await peer.PostAsync(new ViewRequestById(i));

                    if (result is null || result.Payload is not ViewResponse response || response.View is null)
                    {
                        Logger.LogInformation($"View download from {peer.Uri.ToHostname()} failed at height {i}");
                        break;
                    }

                    var blocks = new List<Block>(response.View.Blocks.Count);
                    var votes = new List<Vote>(response.View.Votes.Count);
                    var transactions = new List<TransactionDto>(response.View.Transactions.Count);

                    foreach (var blockhash in response.View.Blocks)
                    {
                        var download = await peer.PostAsync(new BlockRequest(blockhash));

                        if (download is null || download.Payload is not BlockResponse blockResponse || blockResponse.Block is null)
                        {
                            Logger.LogInformation($"Block download from {peer.Uri.ToHostname()} failed at height {i}");
                            goto done;
                        }

                        blocks.Add(blockResponse.Block);
                    }

                    foreach (var votehash in response.View.Votes)
                    {
                        var download = await peer.PostAsync(new VoteRequest(votehash));

                        if (download is null || download.Payload is not VoteResponse voteResponse || voteResponse.Vote is null)
                        {
                            Logger.LogInformation($"Vote download from {peer.Uri.ToHostname()} failed at height {i}");
                            goto done;
                        }

                        votes.Add(voteResponse.Vote);
                    }

                    foreach (var txid in response.View.Transactions)
                    {
                        var download = await peer.PostAsync(new TransactionRequest(txid));

                        if (download is null || download.Payload is not TransactionResponse txResponse || txResponse.Transaction is null)
                        {
                            Logger.LogInformation($"Transaction download from {peer.Uri.ToHostname()} failed at height {i}");
                            goto done;
                        }

                        transactions.Add(txResponse.Transaction);
                    }

                    if (!staging.LoadBlocks(blocks))
                    {
                        brokenChain = true;
                        break;
                    }
                    
                    if (!staging.LoadVotes(votes))
                    {
                        brokenChain = true;
                        break;
                    }

                    if (!staging.LoadTransactions(transactions))
                    {
                        brokenChain = true;
                        break;
                    }

                    if (!staging.LoadView(response.View))
                    {
                        brokenChain = true;
                        break;
                    }
                }

done:
                // If we synchronize from beginning and chain fails, ban client
                if (!findCommonHeight && brokenChain)
                {
                    if(networkManager.Ban(peer.ClientId))
                    {
                        await peer.DisconnectAsync();
                    }

                    return false;
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

    private async Task<long> FindCommonHeight(IStoreManager storeManager, Peer peer, long height)
    {
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
            var view = storeManager.GetView(Math.Max(qHeight, 0));

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

        return heightResponse.CommonHeight;
    }
}
