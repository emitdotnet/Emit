namespace Emit.Abstractions.Observability;

using Emit.Models;

/// <summary>
/// Observes the lifecycle of outbox entries — from enqueue through processing to completion
/// or failure. Implement this interface for outbox-specific metrics, monitoring, or
/// integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// Outbox processing happens outside the middleware pipeline, so these callbacks are
/// invoked directly rather than through middleware. <see cref="OnEnqueuedAsync"/> fires
/// from within the outbox terminal middleware after an entry is persisted.
/// <see cref="OnProcessingAsync"/>, <see cref="OnProcessedAsync"/>, and
/// <see cref="OnProcessErrorAsync"/> fire from the outbox background worker.
/// </para>
/// <para>
/// All methods have default implementations that return <see cref="Task.CompletedTask"/>,
/// so implementors only need to override the callbacks they care about.
/// </para>
/// <para>
/// Observer exceptions are caught and logged individually — a failing observer never
/// blocks other observers or interrupts outbox processing.
/// </para>
/// </remarks>
public interface IOutboxObserver
{
    /// <summary>
    /// Called after an outbox entry has been persisted to the repository.
    /// </summary>
    /// <param name="entry">The outbox entry that was enqueued.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnEnqueuedAsync(OutboxEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called before the outbox worker processes an entry.
    /// </summary>
    /// <param name="entry">The outbox entry about to be processed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProcessingAsync(OutboxEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called after an outbox entry has been successfully processed and deleted.
    /// </summary>
    /// <param name="entry">The outbox entry that was processed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProcessedAsync(OutboxEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when outbox entry processing throws an exception.
    /// </summary>
    /// <param name="entry">The outbox entry that failed.</param>
    /// <param name="exception">The exception thrown during processing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnProcessErrorAsync(OutboxEntry entry, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}
