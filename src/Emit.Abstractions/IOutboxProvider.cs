namespace Emit.Abstractions;

/// <summary>
/// Interface for outbox providers that deliver outbox entries to an external system.
/// </summary>
/// <remarks>
/// <para>
/// Each outbox provider (e.g., Kafka, RabbitMQ) implements this interface
/// to handle the actual delivery of messages to the external system.
/// </para>
/// <para>
/// The worker dispatches entries to the appropriate provider based on
/// the <see cref="Models.OutboxEntry.SystemId"/> field.
/// </para>
/// </remarks>
public interface IOutboxProvider
{
    /// <summary>
    /// Gets the unique identifier for this provider.
    /// </summary>
    /// <remarks>
    /// This ID must match the <see cref="Models.OutboxEntry.SystemId"/> field
    /// for entries that this provider should process.
    /// </remarks>
    string SystemId { get; }

    /// <summary>
    /// Begins delivering a batch of entries for a run of the outbox daemon.
    /// </summary>
    /// <param name="context">The run of the outbox daemon the batch belongs to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The delivery the batch's entries are sent through.</returns>
    /// <remarks>
    /// A provider whose system can fence earlier writers does it for the first delivery of a run, so a run of the
    /// outbox daemon still going on another node, for example after a long pause, can no longer deliver.
    /// </remarks>
    Task<IOutboxDelivery> BeginDeliveryAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default);
}
