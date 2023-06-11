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

public class SyncService : BackgroundService, IBufferService<Chain, SyncService>
{
    private Channel<Chain> SyncChannel { get; } = Channel.CreateBounded<Chain>(3);
    private ILogger<OutgoingTransactionService> Logger { get; }

    public SyncService(ILogger<OutgoingTransactionService> logger)
    {
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
}
