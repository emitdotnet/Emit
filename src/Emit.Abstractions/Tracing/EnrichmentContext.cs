namespace Emit.Abstractions.Tracing;
/// <summary>
/// Provides context information for Activity enrichment.
/// </summary>
public sealed class EnrichmentContext
{
    /// <summary>
    /// Gets the pipeline context: the context of the message being produced or consumed, or of the mediator request.
    /// </summary>
    public required MessageContext MessageContext { get; init; }

    /// <summary>
    /// Gets the tracing phase.
    /// </summary>
    /// <remarks>
    /// One of the values of <see cref="EnrichmentPhases"/>.
    /// </remarks>
    public required string Phase { get; init; }
}
