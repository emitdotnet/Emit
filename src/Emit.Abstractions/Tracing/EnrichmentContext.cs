namespace Emit.Abstractions.Tracing;
/// <summary>
/// Provides context information for Activity enrichment.
/// </summary>
public sealed class EnrichmentContext
{
    /// <summary>
    /// Gets the pipeline context (consume or send).
    /// </summary>
    public required MessageContext MessageContext { get; init; }

    /// <summary>
    /// Gets the tracing phase.
    /// </summary>
    /// <remarks>
    /// Possible values: "produce", "process", "consume", "dlq_publish", "dlq_replay".
    /// </remarks>
    public required string Phase { get; init; }
}
