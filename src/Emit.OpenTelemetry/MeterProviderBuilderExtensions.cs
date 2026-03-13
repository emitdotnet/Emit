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
    // The default OTel SDK histogram boundaries [0, 5, 10, 25, 50, ...] are sized for
    // milliseconds. Emit records all duration instruments in seconds, so without custom
    // boundaries every observation lands in the le=5 bucket and histogram_quantile
    // interpolates nonsense values (e.g. p95 = 4.75 s for a sub-millisecond operation).
    private static readonly double[] SecondBuckets =
        [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private static readonly double[] ByteBuckets =
        [256, 1_024, 4_096, 16_384, 65_536, 524_288, 1_048_576];

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

        if (options.EnableMediatorMeter)
        {
            meters.Add(MeterNames.EmitMediator);
        }

        if (options.EnableKafkaMeter)
        {
            meters.Add(MeterNames.EmitKafka);
        }

        if (options.EnableKafkaBrokerMeter)
        {
            meters.Add(MeterNames.EmitKafkaBroker);
        }

        if (meters.Count > 0)
        {
            builder.AddMeter([.. meters]);
        }

        RegisterHistogramViews(builder);

        var tags = options.GetTags();

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(sp =>
            {
                var nodeIdentity = sp.GetRequiredService<INodeIdentity>();
                var allTags = new KeyValuePair<string, object?>[tags.Length + 1];
                allTags[0] = new("emit.node.id", nodeIdentity.NodeId.ToString());
                tags.Span.CopyTo(allTags.AsSpan(1));
                return new EmitMetricsEnrichment(allTags);
            });
        });

        return builder;
    }

    private static void RegisterHistogramViews(MeterProviderBuilder builder)
    {
        var seconds = new ExplicitBucketHistogramConfiguration { Boundaries = SecondBuckets };
        var bytes = new ExplicitBucketHistogramConfiguration { Boundaries = ByteBuckets };

        // Pipeline
        builder.AddView("emit.pipeline.produce.duration", seconds);
        builder.AddView("emit.pipeline.consume.duration", seconds);

        // Consumer
        builder.AddView("emit.consumer.retry.duration", seconds);
        builder.AddView("emit.consumer.validation.duration", seconds);
        builder.AddView("emit.consumer.circuit_breaker.open_duration", seconds);
        builder.AddView("emit.consumer.rate_limit.wait_duration", seconds);

        // Outbox
        builder.AddView("emit.outbox.processing.duration", seconds);
        builder.AddView("emit.outbox.critical_time", seconds);

        // Lock
        builder.AddView("emit.lock.acquire.duration", seconds);
        builder.AddView("emit.lock.held.duration", seconds);

        // Mediator
        builder.AddView("emit.mediator.send.duration", seconds);

        // Kafka
        builder.AddView("emit.kafka.produce.duration", seconds);
        builder.AddView("emit.kafka.consume.deserialization.duration", seconds);
        builder.AddView("emit.kafka.produce.message_size", bytes);
    }
}
