namespace Emit.Observability;

using System.ComponentModel;
using Emit.Abstractions.Observability;
using Emit.Metrics;
using Emit.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Invokes all registered <see cref="IOutboxObserver"/> instances for outbox lifecycle events.
/// Not a middleware — outbox processing happens outside the pipeline.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OutboxObserverInvoker(
    IEnumerable<IOutboxObserver> observers,
    OutboxMetrics outboxMetrics,
    ILogger<OutboxObserverInvoker> logger)
{
    private readonly IOutboxObserver[] observers = observers.ToArray();

    /// <summary>
    /// Gets a value indicating whether any observers are registered.
    /// </summary>
    public bool HasObservers => observers.Length > 0;

    /// <summary>
    /// Notifies observers that an outbox entry has been enqueued.
    /// </summary>
    public Task OnEnqueuedAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        outboxMetrics.RecordEnqueued(entry.SystemId);
        return observers.InvokeAllAsync(o => o.OnEnqueuedAsync(entry, cancellationToken), logger);
    }

    /// <summary>
    /// Notifies observers that an outbox entry is about to be processed.
    /// </summary>
    public Task OnProcessingAsync(OutboxEntry entry, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnProcessingAsync(entry, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that an outbox entry has been successfully processed and deleted.
    /// </summary>
    public Task OnProcessedAsync(OutboxEntry entry, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnProcessedAsync(entry, cancellationToken), logger);

    /// <summary>
    /// Notifies observers that outbox entry processing failed.
    /// </summary>
    public Task OnProcessErrorAsync(OutboxEntry entry, Exception exception, CancellationToken cancellationToken)
        => observers.InvokeAllAsync(o => o.OnProcessErrorAsync(entry, exception, cancellationToken), logger);
}
