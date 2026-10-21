namespace Emit.Abstractions;

/// <summary>
/// A provider-agnostic event message with a key, value, and optional headers.
/// </summary>
/// <typeparam name="TKey">The event key type.</typeparam>
/// <typeparam name="TValue">The event value type.</typeparam>
/// <param name="Key">The event key, used for partitioning and routing.</param>
/// <param name="Value">The event payload.</param>
/// <param name="Headers">
/// Ordered metadata headers as string key-value pairs. Multiple headers with the same
/// key are permitted. Defaults to an empty collection.
/// </param>
/// <remarks>
/// <para>
/// Headers use string values for simplicity. Providers convert to their native header
/// format internally (e.g., Kafka converts string values to UTF-8 bytes).
/// </para>
/// </remarks>
public sealed record EventMessage<TKey, TValue>(
    TKey Key,
    TValue Value,
    IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    /// <summary>
    /// Initializes a new instance with an empty headers collection.
    /// </summary>
    /// <param name="Key">The event key, used for partitioning and routing.</param>
    /// <param name="Value">The event payload.</param>
    public EventMessage(TKey Key, TValue Value)
        : this(Key, Value, [])
    {
    }

    /// <summary>
    /// Creates a message with a key and no value. Kafka delivers it as a tombstone, which deletes the key on a
    /// log-compacted topic.
    /// </summary>
    /// <param name="key">The key the message is about.</param>
    /// <param name="headers">Optional headers. Defaults to an empty collection.</param>
    /// <returns>A message whose <see cref="Value"/> is null.</returns>
    /// <remarks>
    /// The message is produced like any other: the same pipeline, the same headers added by Emit, and through the
    /// outbox when enabled, in order with the key's other messages. Producing a message with a null value is the
    /// same; this method only saves writing the null.
    /// </remarks>
    public static EventMessage<TKey, TValue> Tombstone(
        TKey key,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null) =>
        new(key, default!, headers ?? []);
}
