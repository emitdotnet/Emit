namespace Emit.Abstractions.Tracing;

/// <summary>
/// The values of <see cref="EnrichmentContext.Phase"/>: which span an enricher is called for.
/// </summary>
public static class EnrichmentPhases
{
    /// <summary>The <c>create</c> span of a message being produced.</summary>
    public const string Produce = "produce";

    /// <summary>The <c>process</c> span of a message, or a batch, being consumed.</summary>
    public const string Consume = "consume";

    /// <summary>The span of a mediator request.</summary>
    public const string Mediator = "mediator";
}
