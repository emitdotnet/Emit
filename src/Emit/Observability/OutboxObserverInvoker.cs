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
    public async Task OnEnqueuedAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        outboxMetrics.RecordEnqueued(entry.SystemId);

        foreach (var observer in observers)
        {
            try
            {
                await observer.OnEnqueuedAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Interface}.{Method} failed for {ObserverType}", nameof(IOutboxObserver), nameof(IOutboxObserver.OnEnqueuedAsync), observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that an outbox entry is about to be processed.
    /// </summary>
    public async Task OnProcessingAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnProcessingAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Interface}.{Method} failed for {ObserverType}", nameof(IOutboxObserver), nameof(IOutboxObserver.OnProcessingAsync), observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that an outbox entry has been successfully processed and deleted.
    /// </summary>
    public async Task OnProcessedAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnProcessedAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Interface}.{Method} failed for {ObserverType}", nameof(IOutboxObserver), nameof(IOutboxObserver.OnProcessedAsync), observer.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Notifies observers that outbox entry processing failed.
    /// </summary>
    public async Task OnProcessErrorAsync(OutboxEntry entry, Exception exception, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnProcessErrorAsync(entry, exception, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Interface}.{Method} failed for {ObserverType}", nameof(IOutboxObserver), nameof(IOutboxObserver.OnProcessErrorAsync), observer.GetType().Name);
            }
        }
    }
}
