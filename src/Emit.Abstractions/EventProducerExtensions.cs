namespace Emit.Abstractions;

/// <summary>
/// Convenience extension methods for <see cref="IEventProducer{TKey, TValue}"/>
/// that accept key, value, and optional headers directly.
/// </summary>
public static class EventProducerExtensions
{
    /// <summary>
    /// Asynchronously produces a message to the configured destination.
    /// </summary>
    /// <param name="producer">The event producer.</param>
    /// <param name="key">The event key, used for partitioning and routing.</param>
    /// <param name="value">The event payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been accepted for delivery.</returns>
    public static Task ProduceAsync<TKey, TValue>(
        this IEventProducer<TKey, TValue> producer,
        TKey key,
        TValue value,
        CancellationToken cancellationToken = default) =>
        producer.ProduceAsync(new EventMessage<TKey, TValue>(key, value), cancellationToken);

    /// <summary>
    /// Asynchronously produces a message with headers to the configured destination.
    /// </summary>
    /// <param name="producer">The event producer.</param>
    /// <param name="key">The event key, used for partitioning and routing.</param>
    /// <param name="value">The event payload.</param>
    /// <param name="headers">Ordered metadata headers as string key-value pairs.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been accepted for delivery.</returns>
    public static Task ProduceAsync<TKey, TValue>(
        this IEventProducer<TKey, TValue> producer,
        TKey key,
        TValue value,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken = default) =>
        producer.ProduceAsync(new EventMessage<TKey, TValue>(key, value, headers), cancellationToken);

}
