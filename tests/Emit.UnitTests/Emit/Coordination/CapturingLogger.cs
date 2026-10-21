namespace Emit.UnitTests.Coordination;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

/// <summary>
/// Logger that records every entry, for tests that assert what a component reports.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<LogEntry> entries = new();

    /// <summary>
    /// Gets the entries logged so far, in order.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries => [.. entries];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
}

/// <summary>
/// One entry recorded by a <see cref="CapturingLogger{T}"/>.
/// </summary>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
