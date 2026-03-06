namespace Emit.Abstractions.Tracing;

using System.Diagnostics;

/// <summary>
/// Enriches Activities with custom tags and baggage.
/// </summary>
/// <remarks>
/// <para>
/// Enrichers are invoked after Emit sets its standard tags, allowing users to add
/// application-specific correlation data (tenant-id, user-id, etc.).
/// </para>
/// <para>
/// Enrichers are registered as scoped services and can inject any dependency.
/// </para>
/// </remarks>
public interface IActivityEnricher
{
    /// <summary>
    /// Enriches the specified Activity with custom tags.
    /// </summary>
    /// <param name="activity">The Activity to enrich.</param>
    /// <param name="context">The enrichment context containing message and phase information.</param>
    void Enrich(Activity activity, EnrichmentContext context);
}
