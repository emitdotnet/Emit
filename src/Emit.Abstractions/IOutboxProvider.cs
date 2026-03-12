namespace Emit.Abstractions;

using Emit.Models;

/// <summary>
/// Interface for outbox providers that process outbox entries.
/// </summary>
/// <remarks>
/// <para>
/// Each outbox provider (e.g., Kafka, RabbitMQ) implements this interface
/// to handle the actual delivery of messages to the external system.
/// </para>
/// <para>
/// The worker dispatches entries to the appropriate provider based on
/// the <see cref="OutboxEntry.SystemId"/> field.
/// </para>
/// </remarks>
public interface IOutboxProvider
{
    /// <summary>
    /// Gets the unique identifier for this provider.
    /// </summary>
    /// <remarks>
    /// This ID must match the <see cref="OutboxEntry.SystemId"/> field
    /// for entries that this provider should process.
    /// </remarks>
    string SystemId { get; }

    /// <summary>
    /// Processes an outbox entry by delivering it to the external system.
    /// </summary>
    /// <param name="entry">The outbox entry to process.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The provider should read the <see cref="OutboxEntry.Body"/>, <see cref="OutboxEntry.Headers"/>,
    /// and <see cref="OutboxEntry.Properties"/> to reconstruct the message and deliver it
    /// to the external system.
    /// </para>
    /// <para>
    /// On success, the method should return normally.
    /// On failure, the method should throw an exception.
    /// </para>
    /// </remarks>
    Task ProcessAsync(OutboxEntry entry, CancellationToken cancellationToken = default);
}
