using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;

namespace Kryolite.Transport.Websocket;

public class TransportException : Exception
{
    public TransportException()
    {

    }

    public TransportException(string message) : base(message)
    {

    }

    [DoesNotReturn]
    public static void Throw()
    {
        throw new TransportException();
    }

    public static void ThrowIfNull([NotNull] WebSocket? socket)
    {
        if (socket is null)
        {
            throw new TransportException();
        }
    }

    [DoesNotReturn]
    public static T ThrowNotRegistered<T>()
    {
        throw new TransportException($"Service for type {typeof(T)} not registered.");
    }

    [DoesNotReturn]
    public static void ThrowNotRegisteredMethod(int methodId)
    {
        throw new TransportException($"Method with id {methodId} not registered.");
    }
}
