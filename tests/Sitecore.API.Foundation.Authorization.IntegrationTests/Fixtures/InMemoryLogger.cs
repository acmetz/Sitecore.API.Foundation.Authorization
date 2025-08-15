using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;

public sealed class InMemoryLogger<T> : ILogger<T>, IDisposable
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _entries.Enqueue(new LogEntry(logLevel, eventId, message, exception));
    }

    public void Dispose() { }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
}
