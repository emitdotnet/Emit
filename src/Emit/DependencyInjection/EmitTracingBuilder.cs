namespace Emit.DependencyInjection;

using System.Diagnostics;
using Emit.Abstractions.Tracing;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builder for configuring distributed tracing.
/// </summary>
public sealed class EmitTracingBuilder
{
    private readonly IServiceCollection services;

    internal EmitTracingBuilder(IServiceCollection services)
    {
        this.services = services;
    }

    /// <summary>
    /// Configures tracing options.
    /// </summary>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public EmitTracingBuilder Configure(Action<EmitTracingOptions> configure)
    {
        services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Adds an Activity enricher (scoped).
    /// </summary>
    /// <typeparam name="TEnricher">The enricher type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public EmitTracingBuilder AddEnricher<TEnricher>() where TEnricher : class, IActivityEnricher
    {
        services.AddScoped<IActivityEnricher, TEnricher>();
        return this;
    }

    /// <summary>
    /// Adds a delegate-based Activity enricher.
    /// </summary>
    /// <param name="enricher">The enrichment delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public EmitTracingBuilder EnrichActivity(Action<Activity, EnrichmentContext> enricher)
    {
        ArgumentNullException.ThrowIfNull(enricher);

        services.AddScoped<IActivityEnricher>(sp => new DelegateActivityEnricher(enricher));
        return this;
    }
}

/// <summary>
/// Internal delegate wrapper for Activity enrichment.
/// </summary>
internal sealed class DelegateActivityEnricher(Action<Activity, EnrichmentContext> enricher) : IActivityEnricher
{
    public void Enrich(Activity activity, EnrichmentContext context) => enricher(activity, context);
}
