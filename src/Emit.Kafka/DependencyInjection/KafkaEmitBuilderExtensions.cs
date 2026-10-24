namespace Emit.Kafka.DependencyInjection;

using System.Diagnostics.Metrics;
using Emit.Abstractions;
using Emit.Abstractions.Metrics;
using Emit.Abstractions.Tracing;
using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Emit.Kafka.Tracing;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Extension methods for configuring Kafka as a provider on <see cref="EmitBuilder"/>.
/// </summary>
public static class KafkaEmitBuilderExtensions
{
    /// <summary>
    /// The shortest transaction timeout, in milliseconds, the Kafka client accepts.
    /// </summary>
    internal const int MinTransactionTimeoutMs = 1_000;

    /// <summary>
    /// The longest transaction timeout, in milliseconds, brokers accept by default
    /// (<c>transaction.max.timeout.ms</c>).
    /// </summary>
    internal const int BrokerMaxTransactionTimeoutMs = 900_000;

    /// <summary>
    /// The transaction timeout, in milliseconds, the Kafka client applies when none is set.
    /// </summary>
    internal const int DefaultTransactionTimeoutMs = 60_000;

    /// <summary>
    /// The time, in milliseconds, the Kafka client waits to batch messages when none is set.
    /// </summary>
    internal const int DefaultLingerMs = 5;

    /// <summary>
    /// The most requests an idempotent producer may have in flight to a broker while keeping each partition's
    /// messages in the order they were produced.
    /// </summary>
    internal const int MaxInFlightKeepingOrder = 5;

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

        var kafkaBuilder = new KafkaBuilder(builder.Services, builder.InboundPipeline, builder.OutboundPipeline);
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

        // Kafka attributes on the create span of each message produced
        builder.Services.AddSingleton<IActivityEnricher, KafkaActivityEnricher>();

        // Validate that ConfigureClient was called
        if (kafkaBuilder.ClientConfigAction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(KafkaBuilder.ConfigureClient)} must be called on the {nameof(KafkaBuilder)} to provide Kafka client configuration.");
        }

        // Always register the shared IProducer<byte[], byte[]> singleton
        RegisterProducer(builder.Services, kafkaBuilder);

        // Registered unconditionally: whether the outbox is enabled is not yet final here, since
        // a persistence provider may be registered after AddKafka. The outbox provider is only
        // ever consumed by the outbox daemon, which core registers solely when the outbox is on,
        // so an unused descriptor costs nothing when it is off.
        RegisterOutboxProvider(builder.Services, kafkaBuilder);
        builder.Services.AddSingleton(new OutboxProviderMarker());

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
        services.AddSingleton(sp => BuildProducer(sp, BuildProducerConfig(kafkaBuilder)));
    }

    /// <summary>
    /// Builds the producer configuration from the client and producer settings.
    /// </summary>
    internal static ConfluentKafka.ProducerConfig BuildProducerConfig(KafkaBuilder kafkaBuilder)
    {
        var config = new ConfluentKafka.ProducerConfig();

        if (kafkaBuilder.ClientConfigAction is { } clientConfigAction)
        {
            var kafkaClientConfig = new KafkaClientOptions();
            clientConfigAction(kafkaClientConfig);
            kafkaClientConfig.ApplyTo(config);
        }

        if (kafkaBuilder.ProducerConfigAction is { } producerConfigAction)
        {
            var kafkaProducerConfig = new KafkaProducerOptions();
            producerConfigAction(kafkaProducerConfig);
            kafkaProducerConfig.ApplyTo(config);
        }

        return config;
    }

    /// <summary>
    /// Builds the configuration of the producer the outbox daemon sends with.
    /// </summary>
    /// <remarks>
    /// The outbox delivers in Kafka transactions, which need idempotence and acknowledgement by all replicas. A
    /// stopped outbox daemon waits for its sends to settle, and must stop within the drain timeout, so a send is given
    /// at most that long. A transaction covers a whole batch, so it keeps the producer's transaction timeout, or
    /// Kafka's default, within what Kafka accepts: no shorter than a send nor <see cref="MinTransactionTimeoutMs"/>,
    /// and no longer than <see cref="BrokerMaxTransactionTimeoutMs"/>, the limit brokers apply by default. A send that
    /// times out is retried from the outbox.
    /// </remarks>
    internal static ConfluentKafka.ProducerConfig BuildOutboxProducerConfig(KafkaBuilder kafkaBuilder, TimeSpan drainTimeout)
    {
        var config = BuildProducerConfig(kafkaBuilder);

        // A drain timeout under a millisecond still bounds the send: zero would mean no limit
        var limit = (int)Math.Clamp(drainTimeout.TotalMilliseconds, 1, BrokerMaxTransactionTimeoutMs);

        // Zero means no limit, and no value means librdkafka's default of five minutes
        if (config.MessageTimeoutMs is not > 0 || config.MessageTimeoutMs > limit)
        {
            config.MessageTimeoutMs = limit;
        }

        // An open transaction holds back what consumers reading committed messages see, until it is aborted
        config.TransactionTimeoutMs = Math.Clamp(
            config.TransactionTimeoutMs ?? DefaultTransactionTimeoutMs,
            Math.Max(config.MessageTimeoutMs.Value, MinTransactionTimeoutMs),
            BrokerMaxTransactionTimeoutMs);

        // The client refuses to create a producer that may wait to batch a message longer than it may take to send it
        config.LingerMs = Math.Min(config.LingerMs ?? DefaultLingerMs, config.MessageTimeoutMs.Value - 1);

        // Idempotence keeps a partition's messages in the order they were produced, with up to five requests in
        // flight, so the outbox hands over a group's entries without waiting for each
        config.EnableIdempotence = true;
        config.Acks = ConfluentKafka.Acks.All;
        config.MaxInFlight = Math.Min(config.MaxInFlight ?? MaxInFlightKeepingOrder, MaxInFlightKeepingOrder);
        return config;
    }

    private static ConfluentKafka.IProducer<byte[], byte[]> BuildProducer(IServiceProvider services, ConfluentKafka.ProducerConfig config)
    {
        var brokerMetrics = services.GetRequiredService<KafkaBrokerMetrics>();
        config.StatisticsIntervalMs ??= 5000;
        return new ConfluentKafka.ProducerBuilder<byte[], byte[]>(config)
            .SetStatisticsHandler((_, json) => brokerMetrics.HandleStatistics(json))
            .Build();
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

    private static void RegisterOutboxProvider(IServiceCollection services, KafkaBuilder kafkaBuilder)
    {
        services.AddSingleton<IOutboxProvider>(sp =>
        {
            var config = BuildOutboxProducerConfig(kafkaBuilder, sp.GetRequiredService<IOptions<DaemonOptions>>().Value.DrainTimeout);
            return new KafkaOutboxProvider(
                transactionalId => BuildProducer(sp, new ConfluentKafka.ProducerConfig(config.ToDictionary()) { TransactionalId = transactionalId }),
                // Starting and committing a transaction may take as long as a send, so a node cut off from the broker
                // gives up within the drain timeout
                TimeSpan.FromMilliseconds(Math.Max(config.MessageTimeoutMs!.Value, MinTransactionTimeoutMs)),
                sp.GetRequiredService<KafkaMetrics>(),
                sp.GetRequiredService<INodeIdentity>(),
                sp.GetRequiredService<IOptions<EmitTracingOptions>>().Value,
                sp.GetRequiredService<ILogger<KafkaOutboxProvider>>());
        });
    }

    private sealed class KafkaMarkerService;
}
