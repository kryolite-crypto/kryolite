using System.Text.Json;
using Kryolite.EventBus;
using Kryolite.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kryolite.Node.API;

public static class EventApi
{
    public static IEndpointRouteBuilder RegisterEventApi(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("chainstate/listen", ListenChainstate);
        builder.MapGet("ledger/{address}/listen", ListenLedger);
        builder.MapGet("contract/{address}/listen", ListenContract);

        return builder;
    }

    private static async Task ListenChainstate(HttpContext ctx, IEventBus eventBus, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        var subId = eventBus.Subscribe<ChainState>(async state =>
        {
            await ctx.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(ctx.Response.Body, state, SharedSourceGenerationContext.Default.ChainState);
            await ctx.Response.WriteAsync($"\n\n");
            await ctx.Response.Body.FlushAsync();
        });

        try
        {
            await ct.WhenCancelled();
        }
        finally
        {
            eventBus.Unsubscribe(subId);
        }
    }

    private static async Task ListenLedger(HttpContext ctx, string address, IEventBus eventBus, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        var addr = (Address)address;
        var subId = eventBus.Subscribe<Ledger>(async ledger =>
        {
            if (ledger.Address != addr)
            {
                return;
            }

            await ctx.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(ctx.Response.Body, ledger, SharedSourceGenerationContext.Default.Ledger);
            await ctx.Response.WriteAsync($"\n\n");
            await ctx.Response.Body.FlushAsync();
        });

        try
        {
            await ct.WhenCancelled();
        }
        finally
        {
            eventBus.Unsubscribe(subId);
        }
    }

    private static async Task ListenContract(HttpContext ctx, string address, IEventBus eventBus, CancellationToken ct)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");

        var addr = (Address)address;
        var sub1 = eventBus.Subscribe<ApprovalEventArgs>(async approval =>
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

            await ctx.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent);
            await ctx.Response.WriteAsync($"\n\n");
            await ctx.Response.Body.FlushAsync();
        });

        var sub2 = eventBus.Subscribe<ConsumeTokenEventArgs>(async consume =>
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

            await ctx.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent);
            await ctx.Response.Body.FlushAsync();
        });

        var sub3 = eventBus.Subscribe<GenericEventArgs>(async generic =>
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

            await ctx.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent);
            await ctx.Response.WriteAsync($"\n\n");
            await ctx.Response.Body.FlushAsync();
        });

        var sub4 = eventBus.Subscribe<TransferTokenEventArgs>(async transfer =>
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

            await ctx.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, NodeSourceGenerationContext.Default.ContractEvent);
            await ctx.Response.WriteAsync($"\n\n");
            await ctx.Response.Body.FlushAsync();
        });

        try
        {
            await ct.WhenCancelled();
        }
        finally
        {
            eventBus.Unsubscribe(sub1);
            eventBus.Unsubscribe(sub2);
            eventBus.Unsubscribe(sub3);
            eventBus.Unsubscribe(sub4);
        }
    }
}
