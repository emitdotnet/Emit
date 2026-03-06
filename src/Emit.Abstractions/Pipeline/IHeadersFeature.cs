namespace Emit.Abstractions.Pipeline;

/// <summary>
/// Provides access to message headers.
/// Present for patterns that carry headers (Kafka, Bus).
/// Absent for patterns without headers (Mediator).
/// </summary>
public interface IHeadersFeature
{
    /// <summary>
    /// Gets the message headers as string key-value pairs.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
}
