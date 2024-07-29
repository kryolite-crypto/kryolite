using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Transport.Websocket;

public static class ServiceResolver
{
    private static Func<WebsocketChannel, IServiceProvider, IWebsocketService>? _generator;
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
    public static IServiceCollection AddKryoliteRpcService(this IServiceCollection builder, Func<WebsocketChannel, IServiceProvider, IWebsocketService> generator)
    {
        _generator = generator;
        return builder;
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

        if (_serviceProvider is null)
        {
            return TransportException.ThrowNotRegistered<IWebsocketService>();
        }

        return _generator(channel, _serviceProvider);
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
