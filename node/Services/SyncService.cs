using Kryolite.Shared;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

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

            HandleSync(chain);
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

    private void HandleSync(Chain chain)
    {
        try
        {
            using var scope = ServiceProvider.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var chainState = storeManager.GetChainState();

            var maxView = chain.Transactions
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .MaxBy(x => BitConverter.ToInt64(x.Data));

            if (maxView is null || maxView.CalculateHash() == chainState.LastHash)
            {
                storeManager.AddTransactionBatch(chain.Transactions, true);
            }

            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            var staging = StagingManager.Create("staging", configuration, loggerFactory);

            var minView = chain.Transactions
                .Where(x => x.TransactionType == TransactionType.VIEW)
                .MinBy(x => BitConverter.ToInt64(x.Data));

            var minHeight = BitConverter.ToInt64(minView?.Data ?? new byte[8]);
            var maxHeight = BitConverter.ToInt64(maxView?.Data ?? new byte[8]);

            Logger.LogInformation("Initalizing staging context");

            BlockchainService.InitializeGenesisBlock(staging, Logger);

            Logger.LogInformation($"Loading local transactions up to height {minHeight}");

            for (var i = 1; i < minHeight; i++)
            {
                var txs = storeManager.GetTransactionsAtHeight(i);

                if (txs.Count > 0)
                {
                    // FIXME: very inefficient
                    staging.LoadTransactions(txs.Select(x => new TransactionDto(x)).ToList());
                }
            }

            Logger.LogInformation($"Staging context loaded to height {staging.GetChainState()?.Height}");
            Logger.LogInformation("Loading remote chain to staging context");

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

            storeManager.LoadStagingChain("staging", newState, staging.StateCache, staging.Events);
        }
        catch (Exception ex)
        {
            Logger.LogInformation(ex, "ChainSync resulted in error");
        }
    }
}
