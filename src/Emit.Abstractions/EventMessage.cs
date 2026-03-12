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
}
