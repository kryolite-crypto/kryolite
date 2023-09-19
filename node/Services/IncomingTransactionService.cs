using Common.Logging;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace Kryolite.Node.Services;

public class IncomingTransactionService : BackgroundService, IBufferService<TransactionDto, IncomingTransactionService>
{
    private Channel<TransactionDto> TxChannel { get; } = Channel.CreateUnbounded<TransactionDto>();
    public IServiceProvider Provider { get; }
    private ILogger<OutgoingTransactionService> Logger { get; }

    public IncomingTransactionService(IServiceProvider provider, ILogger<OutgoingTransactionService> logger)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        /*var items = new List<TransactionDto>(10000);

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await TxChannel.Reader.WaitToReadAsync(stoppingToken);

            if (!result)
            {
                Logger.LogError("TransactionBuffer closed unexpectadly");
                break;
            }

            using var scope = Provider.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            items.Clear();

            while (TxChannel.Reader.TryRead(out var item))
            {
                items.Add(item);

                if (items.Count >= 10000)
                {
                    break;
                }
            }

            manager.AddTransactionBatch(items, false);
        }*/
    }

    public void Add(TransactionDto item)
    {
        //TxChannel.Writer.TryWrite(item);
        using var scope = Provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        manager.AddTransaction(item, true);
    }

    public void Add(List<TransactionDto> items)
    {
        /*foreach (var item in items)
        {
            TxChannel.Writer.TryWrite(item);
        }*/

        using var scope = Provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        manager.AddTransactionBatch(items, true);
    }

    public async Task AddAsync(TransactionDto item)
    {
        //await TxChannel.Writer.WriteAsync(item);
        using var scope = Provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        manager.AddTransaction(item, true);

        await Task.CompletedTask;
    }

    public async Task AddAsync(List<TransactionDto> items)
    {
        /*foreach (var item in items)
        {
            await TxChannel.Writer.WriteAsync(item);
        }*/

        using var scope = Provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        manager.AddTransactionBatch(items, true);

        await Task.CompletedTask;
    }
}
