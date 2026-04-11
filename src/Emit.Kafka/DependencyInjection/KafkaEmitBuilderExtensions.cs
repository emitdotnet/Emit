namespace Emit.Kafka.DependencyInjection;

using System.Diagnostics.Metrics;
using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Extension methods for configuring Kafka as a provider on <see cref="EmitBuilder"/>.
/// </summary>
public static class KafkaEmitBuilderExtensions
{
    /// <summary>
    /// Adds the Kafka provider registration. Supports both producing and consuming.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <c>AddKafka</c> has already been called, or if
    /// <see cref="KafkaBuilder.ConfigureClient"/> was not called.
    /// </exception>
    public static EmitBuilder AddKafka(this EmitBuilder builder, Action<KafkaBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotAlreadyRegistered(builder.Services);

        var kafkaBuilder = new KafkaBuilder(builder.Services, builder.OutboxEnabled, builder.InboundPipeline, builder.OutboundPipeline);
        configure(kafkaBuilder);

        // Register Kafka-level middleware with appropriate lifetimes
        kafkaBuilder.InboundPipeline.RegisterServices(builder.Services);
        kafkaBuilder.OutboundPipeline.RegisterServices(builder.Services);

        // Register Kafka consumer observer invoker (zero overhead when no observers registered)
        builder.Services.AddSingleton<KafkaConsumerObserverInvoker>();

        // Register Kafka metrics
        builder.Services.TryAddSingleton(sp => new KafkaMetrics(
            sp.GetService<IMeterFactory>(),
            sp.GetRequiredService<EmitMetricsEnrichment>()));

        // Register broker metrics (librdkafka statistics)
        builder.Services.TryAddSingleton(sp => new KafkaBrokerMetrics(
            sp.GetService<IMeterFactory>(),
            sp.GetRequiredService<EmitMetricsEnrichment>()));

        // Validate that ConfigureClient was called
        if (kafkaBuilder.ClientConfigAction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(KafkaBuilder.ConfigureClient)} must be called on the {nameof(KafkaBuilder)} to provide Kafka client configuration.");
        }

        // Always register the shared IProducer<byte[], byte[]> singleton
        RegisterProducer(builder.Services, kafkaBuilder);

        // Register outbox provider and marker only when outbox mode is enabled
        if (builder.OutboxEnabled)
        {
            RegisterOutboxProvider(builder.Services);
            builder.Services.AddSingleton(new OutboxProviderMarker());
        }

        // Register AdminClient singleton and topic verifier at position 0
        RegisterAdminClient(builder.Services, kafkaBuilder);
        RegisterTopicVerifier(builder.Services, kafkaBuilder);

        return builder;
    }

    private static void EnsureNotAlreadyRegistered(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(KafkaMarkerService)))
        {
            throw new InvalidOperationException(
                $"{nameof(AddKafka)} has already been called. Only one Kafka provider registration is allowed.");
        }

        services.AddSingleton<KafkaMarkerService>();
    }

    private static void RegisterProducer(IServiceCollection services, KafkaBuilder kafkaBuilder)
    {
        var clientConfigAction = kafkaBuilder.ClientConfigAction;
        var producerConfigAction = kafkaBuilder.ProducerConfigAction;

        services.AddSingleton(sp =>
        {
            var config = new ConfluentKafka.ProducerConfig();

            if (clientConfigAction is not null)
            {
                var kafkaClientConfig = new KafkaClientOptions();
                clientConfigAction(kafkaClientConfig);
                kafkaClientConfig.ApplyTo(config);
            }

            if (producerConfigAction is not null)
            {
                var kafkaProducerConfig = new KafkaProducerOptions();
                producerConfigAction(kafkaProducerConfig);
                kafkaProducerConfig.ApplyTo(config);
            }

            var brokerMetrics = sp.GetRequiredService<KafkaBrokerMetrics>();
            config.StatisticsIntervalMs ??= 5000;
            return new ConfluentKafka.ProducerBuilder<byte[], byte[]>(config)
                .SetStatisticsHandler((_, json) => brokerMetrics.HandleStatistics(json))
                .Build();
        });
    }

    private static void RegisterAdminClient(IServiceCollection services, KafkaBuilder kafkaBuilder)
    {
        var clientConfigAction = kafkaBuilder.ClientConfigAction;

        services.TryAddSingleton<ConfluentKafka.IAdminClient>(sp =>
        {
            var config = new ConfluentKafka.AdminClientConfig();

            if (clientConfigAction is not null)
            {
                var kafkaClientConfig = new KafkaClientOptions();
                clientConfigAction(kafkaClientConfig);
                kafkaClientConfig.ApplyTo(config);
            }

            return new ConfluentKafka.AdminClientBuilder(config).Build();
        });
    }

    private static void RegisterTopicVerifier(IServiceCollection services, KafkaBuilder kafkaBuilder)
    {
        var requiredTopics = kafkaBuilder.GetRequiredTopics();
        var autoProvision = kafkaBuilder.AutoProvisionEnabled;
        var provisioningConfigs = kafkaBuilder.GetProvisioningConfigs();

        // Insert at position 0 so it runs before all other hosted services
        services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(sp =>
            new KafkaTopicVerifier(
                sp.GetRequiredService<ConfluentKafka.IAdminClient>(),
                requiredTopics,
                autoProvision,
                provisioningConfigs,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<KafkaTopicVerifier>())));
    }

    private static void RegisterOutboxProvider(IServiceCollection services)
    {
        services.AddSingleton<IOutboxProvider, KafkaOutboxProvider>();
    }

    private sealed class KafkaMarkerService;
}
