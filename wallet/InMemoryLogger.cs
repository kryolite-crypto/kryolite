using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Kryolite.Wallet;

public class InMemoryLogger : ILogger
{
    public static ConcurrentQueue<string> Messages = new();
    public static EventHandler<string>? OnNewMessage;

    public IDisposable BeginScope<TState>(TState state)
    {
        return new NoopDisposable();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = string.Empty;

        message = $"{DateTime.Now} {logLevel.ToString()}: {state}";

        if (exception != null)
        {
            message += Environment.NewLine;
            message += exception.ToString();
        }

        if (Messages.Count > 10000) {
            Messages.TryDequeue(out var _);
        }

        Messages.Enqueue(message);

        OnNewMessage?.Invoke(this, message);
    }

    private class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

public class InMemoryLoggerProvider : ILoggerProvider
{     
    public ILogger CreateLogger(string categoryName)
    {                
        return new InMemoryLogger();
    }

    public void Dispose()
    {
    }
}