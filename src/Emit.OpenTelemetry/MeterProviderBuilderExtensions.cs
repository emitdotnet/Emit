namespace Emit.OpenTelemetry;

using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using global::OpenTelemetry.Metrics;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Emit instrumentation with OpenTelemetry.
/// </summary>
public static class MeterProviderBuilderExtensions
{
    /// <summary>
    /// Adds Emit instrumentation to the OpenTelemetry metrics pipeline.
    /// Subscribes to all enabled Emit meters and configures static tag enrichment.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <param name="configure">Optional configuration for meter selection and tag enrichment.</param>
    /// <returns>The meter provider builder for method chaining.</returns>
    public static MeterProviderBuilder AddEmitInstrumentation(
        this MeterProviderBuilder builder,
        Action<EmitInstrumentationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new EmitInstrumentationOptions();
        configure?.Invoke(options);

        List<string> meters = [];

        if (options.EnableEmitMeter)
        {
            meters.Add(MeterNames.Emit);
        }

        if (options.EnableOutboxMeter)
        {
            meters.Add(MeterNames.EmitOutbox);
        }

        if (options.EnableLockMeter)
        {
            meters.Add(MeterNames.EmitLock);
        }

        if (options.EnableDaemonMeter)
        {
            meters.Add(MeterNames.EmitDaemon);
        }

        if (meters.Count > 0)
        {
            builder.AddMeter([.. meters]);
        }

        // The meters of the optional packages in use, which register them; every builder OpenTelemetry creates
        // configures itself from the service provider this way
        (builder as IDeferredMeterProviderBuilder)?.Configure((services, meterProvider) =>
        {
            string[] packageMeters =
            [
                .. services.GetServices<PackageMeter>().Select(m => m.Name).Where(name => !options.IsDisabled(name)),
            ];
            if (packageMeters.Length > 0)
            {
                meterProvider.AddMeter(packageMeters);
            }
        });

        var tags = options.GetTags();

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(sp =>
            {
                var nodeIdentity = sp.GetRequiredService<INodeIdentity>();
                var allTags = new KeyValuePair<string, object?>[tags.Length + 2];
                allTags[0] = new("emit.node.id", nodeIdentity.NodeId.ToString());
                allTags[1] = new("emit.service.name", nodeIdentity.ServiceName);
                tags.Span.CopyTo(allTags.AsSpan(2));
                return new EmitMetricsEnrichment(allTags);
            });
        });

        return builder;
    }
}
