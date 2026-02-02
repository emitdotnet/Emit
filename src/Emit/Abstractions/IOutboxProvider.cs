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
/// the <see cref="OutboxEntry.ProviderId"/> field.
/// </para>
/// </remarks>
public interface IOutboxProvider
{
    /// <summary>
    /// Gets the unique identifier for this provider.
    /// </summary>
    /// <remarks>
    /// This ID must match the <see cref="OutboxEntry.ProviderId"/> field
    /// for entries that this provider should process.
    /// </remarks>
    string ProviderId { get; }

    /// <summary>
    /// Processes an outbox entry by delivering it to the external system.
    /// </summary>
    /// <param name="entry">The outbox entry to process.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The provider should deserialize the <see cref="OutboxEntry.Payload"/>
    /// and deliver the message to the external system.
    /// </para>
    /// <para>
    /// The <see cref="OutboxEntry.RegistrationKey"/> identifies which
    /// producer registration to use for delivery.
    /// </para>
    /// <para>
    /// On success, the method should return normally.
    /// On failure, the method should throw an exception.
    /// </para>
    /// </remarks>
    Task ProcessAsync(OutboxEntry entry, CancellationToken cancellationToken = default);
}
