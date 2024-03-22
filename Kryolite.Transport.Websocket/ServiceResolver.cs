using Kryolite.ByteSerializer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kryolite.Transport.Websocket;

public static class ServiceResolver
{
    private static Func<WebsocketChannel, IWebsocketService>? _generator;
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Register service provider
    /// </summary>
    /// <param name="id"></param>
    /// <param name="generator"></param>
    public static void UseKryoliteRpc(this IApplicationBuilder builder)
    {
        _serviceProvider = builder.ApplicationServices;
    }

    /// <summary>
    /// Register generator function
    /// </summary>
    /// <param name="path"></param>
    /// <param name="generator"></param>
    public static void MapKryoliteRpcService(this IEndpointRouteBuilder builder, string path, Func<WebsocketChannel, IWebsocketService> generator)
    {
        _generator = generator;

        builder.MapGet(path, async (HttpContext ctx, CancellationToken cancellationToken) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!Authorize(ctx))
            {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
            }

            using var webSocket = await ctx.WebSockets.AcceptWebSocketAsync();

            var tcs = new TaskCompletionSource();

            cancellationToken.Register(() => tcs.TrySetResult());
            await tcs.Task;
        });
    }

    /// <summary>
    /// Resolve generic service for channel
    /// </summary>
    /// <param name="path"></param>
    /// <param name="channel"></param>
    /// <returns></returns>
    public static IWebsocketService Resolve(WebsocketChannel channel)
    {
        if (_generator is null)
        {
            return TransportException.ThrowNotRegistered<IWebsocketService>();
        }

        return _generator(channel);
    }

    /// <summary>
    /// Create client service for channel
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="channel"></param>
    /// <returns></returns>
    public static T CreateClient<T>(WebsocketChannel channel) where T : IWebsocketService<T>
    {
        if (_serviceProvider is null)
        {
            return TransportException.ThrowNotRegistered<T>();
        }
    
        return T.CreateClient(channel, _serviceProvider);
    }
}
