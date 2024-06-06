using System.Net;
using System.Text.Json;
using Kryolite.EventBus;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Kryolite.Node.API;

public static class EventApi
{
    public static IEndpointRouteBuilder RegisterEventApi(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("chainstate/listen", ListenChainstate);
        builder.MapGet("ledger/{address}/listen", ListenLedger);
        builder.MapGet("contract/{address}/listen", ListenContract);
        builder.MapGet("blocktemplate/{address}/listen", ListenBlockTemplate);

        return builder;
    }

    private static async Task ListenChainstate(HttpContext ctx, IEventBus eventBus, IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, ct);
        var token = cts.Token;

        using var subId = eventBus.Subscribe<ChainState>(async state =>
        {
            await ctx.Response.WriteAsync($"data: ", token);
            await JsonSerializer.SerializeAsync(ctx.Response.Body, new ChainStateDto(state), SharedSourceGenerationContext.Default.ChainStateDto, token);
            await ctx.Response.WriteAsync($"\n\n", token);
            await ctx.Response.Body.FlushAsync(token);
        });

        await token.WhenCancelled();
    }

    private static async Task ListenBlockTemplate(HttpContext ctx, string address, IEventBus eventBus, IStoreManager storeManager, IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, ct);
        var token = cts.Token;

        var template = storeManager.GetBlocktemplate(address.ToString());
        await PublishBlockTemplate(ctx, template, token);

        using var subId = eventBus.Subscribe<ChainState>(async state =>
        {
            var blocktemplate = storeManager.GetBlocktemplate(address.ToString());
            await PublishBlockTemplate(ctx, blocktemplate, token);
        });

        await token.WhenCancelled();
    }

    private static async Task PublishBlockTemplate(HttpContext ctx, BlockTemplate blocktemplate, CancellationToken cancellationToken)
    {
        if (blocktemplate is null)
        {
            return;
        }

        await ctx.Response.WriteAsync($"data: ", cancellationToken);
        await JsonSerializer.SerializeAsync(ctx.Response.Body, blocktemplate, SharedSourceGenerationContext.Default.BlockTemplate, cancellationToken);
        await ctx.Response.WriteAsync($"\n\n", cancellationToken);
        await ctx.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task ListenLedger(HttpContext ctx, string address, IEventBus eventBus, IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, ct);
        var token = cts.Token;

        var addr = (Address)address;
        using var subId = eventBus.Subscribe<Ledger>(async ledger =>
        {
            if (ledger.Address != addr)
            {
                return;
            }

            await ctx.Response.WriteAsync($"data: ", token);
            await JsonSerializer.SerializeAsync(ctx.Response.Body, ledger, SharedSourceGenerationContext.Default.Ledger, token);
            await ctx.Response.WriteAsync($"\n\n", token);
            await ctx.Response.Body.FlushAsync(token);
        });

        await token.WhenCancelled();
    }

    private static async Task ListenContract(HttpContext ctx, string address, IEventBus eventBus, IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, ct);
        var token = cts.Token;

        var addr = (Address)address;
        using var sub1 = eventBus.Subscribe<ApprovalEventArgs>(async approval =>
        {
            if (approval.Contract != addr)
            {
                return;
            }

            var payload = new ContractEvent
            {
                Type = "Approval",
                Event = approval
            };

            await ctx.Response.WriteAsync($"data: ", token);
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent, token);
            await ctx.Response.WriteAsync($"\n\n", token);
            await ctx.Response.Body.FlushAsync(token);
        });

        using var sub2 = eventBus.Subscribe<ConsumeTokenEventArgs>(async consume =>
        {
            if (consume.Contract != addr)
            {
                return;
            }

            var payload = new ContractEvent
            {
                Type = "ConsumeToken",
                Event = consume
            };

            await ctx.Response.WriteAsync($"data: ", token);
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent, token);
            await ctx.Response.Body.FlushAsync(token);
        });

        using var sub3 = eventBus.Subscribe<GenericEventArgs>(async generic =>
        {
            if (generic.Contract != addr)
            {
                return;
            }

            var payload = new ContractEvent
            {
                Type = "Custom",
                Event = generic
            };

            await ctx.Response.WriteAsync($"data: ", token);
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent, token);
            await ctx.Response.WriteAsync($"\n\n", token);
            await ctx.Response.Body.FlushAsync(token);
        });

        using var sub4 = eventBus.Subscribe<TransferTokenEventArgs>(async transfer =>
        {
            if (transfer.Contract != addr)
            {
                return;
            }

            var payload = new ContractEvent
            {
                Type = "TransferToken",
                Event = transfer
            };

            await ctx.Response.WriteAsync($"data: ", token);
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent, token);
            await ctx.Response.WriteAsync($"\n\n", token);
            await ctx.Response.Body.FlushAsync(token);
        });

        await token.WhenCancelled();
    }
}
