using Kryolite.Shared.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Kryolite.Node.Services;

public class TransactionService : BackgroundService, IBufferService<TransactionDto>
{
    private Channel<TransactionDto> TxChannel { get; } = Channel.CreateUnbounded<TransactionDto>();
    private IMeshNetwork MeshNetwork { get; }
    private ILogger<TransactionService> Logger { get; }

    public TransactionService(IMeshNetwork meshNetwork, ILogger<TransactionService> logger)
    {
        MeshNetwork = meshNetwork ?? throw new ArgumentNullException(nameof(meshNetwork));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await TxChannel.Reader.WaitToReadAsync(stoppingToken);

            if (!result)
            {
                Logger.LogError("TransactionBuffer closed unexpectadly");
                break;
            }

            // buffer transactions for few moments
            await Task.Delay(100);

            var items = TxChannel.Reader.ReadAllAsync(stoppingToken)
                .ToBlockingEnumerable(stoppingToken);

            var msg = new TransactionBatch
            {
                Transactions = items.ToList()
            };

            await MeshNetwork.BroadcastAsync(msg);
        }
    }

    public void Add(TransactionDto item)
    {
        TxChannel.Writer.TryWrite(item);
    }

    public void Add(List<TransactionDto> items)
    {
        foreach (var item in items)
        {
            TxChannel.Writer.TryWrite(item);
        }
    }
}
