namespace Emit.Kafka.Observability;

/// <summary>
/// Observes Kafka consumer lifecycle events that occur outside the message processing pipeline.
/// Implement this interface for consumer health monitoring, partition tracking, offset commit
/// verification, or integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// All methods have default implementations that return <see cref="Task.CompletedTask"/>,
/// so implementors only need to override the callbacks they care about.
/// </para>
/// <para>
/// Observer exceptions are caught and logged individually — a failing observer never
/// blocks other observers or interrupts consumer operation.
/// </para>
/// </remarks>
public interface IKafkaConsumerObserver
{
    /// <summary>
    /// Called when a consumer group worker starts polling.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConsumerStartedAsync(ConsumerStartedEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when a consumer group worker stops.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConsumerStoppedAsync(ConsumerStoppedEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when a consumer worker faults and triggers a pool restart.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConsumerFaultedAsync(ConsumerFaultedEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when partitions are assigned during a rebalance.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPartitionsAssignedAsync(PartitionsAssignedEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when partitions are gracefully revoked during a rebalance.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPartitionsRevokedAsync(PartitionsRevokedEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when partitions are lost (unclean partition loss).
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnPartitionsLostAsync(PartitionsLostEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called after offsets are successfully committed.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnOffsetsCommittedAsync(OffsetsCommittedEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when an offset commit fails.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnOffsetCommitErrorAsync(OffsetCommitErrorEvent e) => Task.CompletedTask;

    /// <summary>
    /// Called when message deserialization fails before pipeline entry.
    /// </summary>
    /// <param name="e">The event details.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnDeserializationErrorAsync(DeserializationErrorEvent e) => Task.CompletedTask;
}
