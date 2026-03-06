namespace Emit.Tracing;

using System.Diagnostics;
using Emit.Abstractions.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Invokes registered Activity enrichers with error handling.
/// </summary>
internal sealed class ActivityEnricherInvoker(ILogger<ActivityEnricherInvoker> logger)
{
    /// <summary>
    /// Invokes all registered enrichers for the specified Activity.
    /// </summary>
    /// <param name="activity">The Activity to enrich.</param>
    /// <param name="context">The enrichment context.</param>
    /// <param name="serviceProvider">The service provider to resolve enrichers from.</param>
    public void InvokeEnrichers(Activity activity, EnrichmentContext context, IServiceProvider serviceProvider)
    {
        var enrichers = serviceProvider.GetServices<IActivityEnricher>();

        foreach (var enricher in enrichers)
        {
            try
            {
                enricher.Enrich(activity, context);
            }
            catch (Exception ex)
            {
                // Log warning but don't throw - enrichers should not break the pipeline
                logger.LogWarning(ex,
                    "Activity enricher {EnricherType} threw an exception during {Phase} phase",
                    enricher.GetType().Name, context.Phase);
            }
        }
    }
}
