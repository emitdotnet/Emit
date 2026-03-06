namespace Emit.Abstractions;

/// <summary>
/// Provides access to distributed tracing context captured from the current Activity.
/// </summary>
/// <remarks>
/// This feature is set by tracing middleware to propagate W3C Trace Context
/// through the outbound pipeline to the outbox terminal.
/// </remarks>
public interface IActivityFeature
{
    /// <summary>
    /// Gets the W3C traceparent value from the current Activity.
    /// </summary>
    /// <remarks>
    /// Format: version-traceid-spanid-flags (e.g., "00-abc123...-def456...-01")
    /// </remarks>
    string? TraceParent { get; }

    /// <summary>
    /// Gets the optional W3C tracestate value from the current Activity.
    /// </summary>
    string? TraceState { get; }

    /// <summary>
    /// Gets the Activity baggage (correlation data) to propagate.
    /// </summary>
    IEnumerable<KeyValuePair<string, string>> Baggage { get; }
}
