namespace Kryolite.Transport.Websocket;

public interface IWebsocketService<T> : IWebsocketService
{
    static abstract T CreateClient(WebsocketChannel channel, IServiceProvider serviceProvider);
}

public interface IWebsocketService
{
    ArraySegment<byte> CallMethod(byte method, ArraySegment<byte> payload);
}
