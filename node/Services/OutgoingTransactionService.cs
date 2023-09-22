using Kryolite.Shared.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Kryolite.Node.Services;

public class OutgoingTransactionService : BackgroundService, IBufferService<TransactionDto, OutgoingTransactionService>
{
    private Channel<TransactionDto> TxChannel { get; } = Channel.CreateUnbounded<TransactionDto>();
    private IMeshNetwork MeshNetwork { get; }
    private ILogger<OutgoingTransactionService> Logger { get; }

    public OutgoingTransactionService(IMeshNetwork meshNetwork, ILogger<OutgoingTransactionService> logger)
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

            var items = new List<TransactionDto>();

            while (TxChannel.Reader.TryRead(out var item))
            {
                items.Add(item);

                if (items.Count >= 10_000)
                {
                    break;
                }
            }

            var msg = new TransactionBroadcast
            {
                Transactions = items.ToList()
            };

            await MeshNetwork.BroadcastAsync(msg);
        }
    }

    public void Add(TransactionDto item)
    {
        _ = TxChannel.Writer.TryWrite(item);
    }

    public void Add(List<TransactionDto> items)
    {
        foreach (var item in items)
        {
            TxChannel.Writer.TryWrite(item);
        }
    }

    public Task AddAsync(TransactionDto item)
    {
        throw new NotImplementedException();
    }

    public Task AddAsync(List<TransactionDto> items)
    {
        throw new NotImplementedException();
    }
}
