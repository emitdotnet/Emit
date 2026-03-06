namespace Emit.Abstractions;

/// <summary>
/// Produces events to a destination configured at registration time.
/// </summary>
/// <typeparam name="TKey">The event key type.</typeparam>
/// <typeparam name="TValue">The event value type.</typeparam>
public interface IEventProducer<TKey, TValue>
{
    /// <summary>
    /// Asynchronously produces a message to the configured destination.
    /// </summary>
    /// <param name="message">The message to produce.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been accepted for delivery.</returns>
    Task ProduceAsync(
        EventMessage<TKey, TValue> message,
        CancellationToken cancellationToken = default);

}
