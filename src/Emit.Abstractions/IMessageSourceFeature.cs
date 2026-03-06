namespace Emit.Abstractions;

/// <summary>
/// Provides transport-agnostic source metadata for a consumed message.
/// Middleware and observers use this feature to enrich logs and dead-letter headers
/// without coupling to a specific messaging technology.
/// </summary>
/// <remarks>
/// Each provider populates <see cref="Properties"/> with technology-specific metadata.
/// For example, a Kafka provider includes <c>topic</c>, <c>partition</c>, and <c>offset</c>.
/// </remarks>
public interface IMessageSourceFeature
{
    /// <summary>
    /// Key-value pairs describing the source of the message.
    /// Keys are lowercase identifiers (e.g., <c>"topic"</c>, <c>"partition"</c>, <c>"offset"</c>).
    /// Values are string representations of the source metadata.
    /// </summary>
    IReadOnlyDictionary<string, string> Properties { get; }
}
