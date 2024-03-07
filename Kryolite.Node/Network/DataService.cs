using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kryolite.EventBus;
using Kryolite.Grpc.DataService;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class DataService : IDataService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DataService> _logger;
    private readonly CancellationTokenSource _cts = new();

    public DataService(IServiceProvider sp, IHostApplicationLifetime lifetime, ILogger<DataService> logger)
    {
        _sp = sp;
        _logger = logger;

        lifetime.ApplicationStopping.Register(() => _cts.Cancel());
    }

    public ExecutionResult AddTransaction(TransactionDto transaction)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        return storeManager.AddTransaction(transaction, true);
    }

    public Validator? GetValidator(Address address)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        return storeManager.GetStake(address);
    }

    public bool PostSolution(BlockTemplate blockTemplate)
    {
        using var scope = _sp.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        return storeManager.AddBlock(blockTemplate, true);
    }

    public async IAsyncEnumerable<BlockTemplate> SubscribeToBlockTemplates(Address address, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Address.IsValid(address.ToString()))
        {
            yield break;
        }

        var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token).Token;
        var channel = Channel.CreateUnbounded<ChainState>();

        using var scope = _sp.CreateScope();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        yield return storeManager.GetBlocktemplate(address);

        using var sub = eventBus.Subscribe<ChainState>(async (chainState) =>
        {
            await channel.Writer.WriteAsync(chainState);
        });

        while (!token.IsCancellationRequested)
        {
            ChainState? data = null;

            try
            {
                var open = await channel.Reader.WaitToReadAsync(token);

                if (!open)
                {
                    break;
                }

                data = await channel.Reader.ReadAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Client receive stream terminated with exception");
                break;
            }

            if (data is not null)
            {
                yield return storeManager.GetBlocktemplate(address);
            }
        }
    }
}