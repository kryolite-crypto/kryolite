using System.Diagnostics.CodeAnalysis;

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
