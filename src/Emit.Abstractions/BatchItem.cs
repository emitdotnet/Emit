namespace Emit.Abstractions;

/// <summary>
/// A single item within a <see cref="MessageBatch{T}"/>. Carries the deserialized message
/// and the original transport context with raw bytes, headers, and provider-specific metadata.
/// </summary>
/// <typeparam name="T">The message value type.</typeparam>
public sealed class BatchItem<T>
{
    /// <summary>
    /// The deserialized message payload.
    /// </summary>
    public required T Message { get; init; }

    /// <summary>
    /// The original transport context for this individual message.
    /// Contains raw key/value bytes, headers, and provider-specific metadata such as partition and offset.
    /// </summary>
    public required TransportContext TransportContext { get; init; }
}
