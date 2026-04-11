namespace Emit.Kafka.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.Kafka.Consumer;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Emit.Metrics;
using Emit.Observability;
using Emit.Pipeline;
using Emit.Pipeline.Modules;
using Emit.Routing;
using Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

/// <summary>
/// Configures the Kafka provider within an <see cref="Emit.DependencyInjection.EmitBuilder"/>.
/// Provides shared client configuration and topic declarations.
/// </summary>
public sealed class KafkaBuilder : IInboundPipelineConfigurable, IOutboundPipelineConfigurable
{
    private readonly IServiceCollection services;
    private readonly bool outboxEnabled;
    private readonly IMessagePipelineBuilder globalInboundPipeline;
    private readonly IMessagePipelineBuilder globalOutboundPipeline;
    private readonly HashSet<string> registeredTopicNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TopicCreationOptions> provisioningConfigs = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the Kafka-level inbound middleware pipeline builder. Middleware registered here
    /// wraps all Kafka consumer handlers across all consumer groups.
    /// </summary>
    public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// Gets the Kafka-level outbound middleware pipeline builder. Middleware registered here
    /// wraps all Kafka producers across all topics.
    /// </summary>
    public IMessagePipelineBuilder OutboundPipeline { get; } = new MessagePipelineBuilder();

    /// <summary>
    /// The stored client configuration action. Applied to both producer and consumer configs.
    /// </summary>
    internal Action<KafkaClientConfig>? ClientConfigAction { get; private set; }

    /// <summary>
    /// The stored producer configuration action. Applied to the shared producer config.
    /// </summary>
    internal Action<KafkaProducerConfig>? ProducerConfigAction { get; private set; }

    /// <summary>
    /// The stored schema registry configuration action.
    /// </summary>
    internal Action<KafkaSchemaRegistryConfig>? SchemaRegistryConfigAction { get; private set; }

    /// <summary>
    /// Whether auto-provisioning of missing topics is enabled.
    /// </summary>
    internal bool AutoProvisionEnabled { get; private set; }

    /// <summary>
    /// Creates a new Kafka builder.
    /// </summary>
    internal KafkaBuilder(
        IServiceCollection services,
        bool outboxEnabled,
        IMessagePipelineBuilder globalInboundPipeline,
        IMessagePipelineBuilder globalOutboundPipeline)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.outboxEnabled = outboxEnabled;
        this.globalInboundPipeline = globalInboundPipeline;
        this.globalOutboundPipeline = globalOutboundPipeline;
    }

    /// <summary>
    /// Configures shared Kafka client settings (bootstrap servers, timeouts, security).
    /// Must be called exactly once.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public KafkaBuilder ConfigureClient(Action<KafkaClientConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (ClientConfigAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigureClient)} has already been called. Only one client configuration is allowed per Kafka registration.");
        }

        ClientConfigAction = configure;
        return this;
    }

    /// <summary>
    /// Configures shared Kafka producer settings (acks, batching, compression, idempotence).
    /// These settings apply to the single shared <c>IProducer</c> instance used by all topics.
    /// Must be called at most once.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public KafkaBuilder ConfigureProducer(Action<KafkaProducerConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (ProducerConfigAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigureProducer)} has already been called. Only one producer configuration is allowed per Kafka registration.");
        }

        ProducerConfigAction = configure;
        return this;
    }

    /// <summary>
    /// Configures a schema registry client. Must be called at most once.
    /// Registers an <see cref="ConfluentSchemaRegistry.ISchemaRegistryClient"/> singleton in DI.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public KafkaBuilder ConfigureSchemaRegistry(Action<KafkaSchemaRegistryConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (SchemaRegistryConfigAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigureSchemaRegistry)} has already been called. Only one schema registry configuration is allowed per Kafka registration.");
        }

        SchemaRegistryConfigAction = configure;
        services.AddSingleton<SchemaRegistryMarker>();
        RegisterSchemaRegistryClient(configure);
        return this;
    }

    /// <summary>
    /// Registers a Kafka consumer lifecycle observer that is notified of consumer
    /// start/stop, partition rebalancing, offset commits, and deserialization errors.
    /// </summary>
    /// <typeparam name="T">The observer type.</typeparam>
    /// <returns>This builder for method chaining.</returns>
    public KafkaBuilder AddConsumerObserver<T>() where T : class, IKafkaConsumerObserver
    {
        services.AddSingleton<IKafkaConsumerObserver, T>();
        return this;
    }

    /// <summary>
    /// Enables automatic creation of missing topics at application startup.
    /// Per-topic creation settings can be configured via
    /// <see cref="KafkaTopicBuilder{TKey,TValue}.Provisioning"/>.
    /// </summary>
    public KafkaBuilder AutoProvision()
    {
        AutoProvisionEnabled = true;
        return this;
    }

    /// <summary>
    /// Configures a dead letter topic. All dead-lettered messages from any consumer group are
    /// produced to this topic. Delegates to <see cref="Topic{TKey,TValue}"/> with <c>byte[]</c>
    /// key/value types, so the DLQ topic participates in all standard topic infrastructure
    /// (verification, provisioning, consumer groups).
    /// </summary>
    /// <param name="topicName">The dead letter topic name.</param>
    /// <param name="configure">Optional configuration for the DLQ topic (consumer groups, provisioning).</param>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public KafkaBuilder DeadLetter(string topicName, Action<KafkaTopicBuilder<byte[], byte[]>>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);

        if (services.Any(d => d.ServiceType == typeof(IDeadLetterSink)))
        {
            throw new InvalidOperationException(
                $"{nameof(DeadLetter)} has already been called. Only one dead letter configuration is allowed per Kafka registration.");
        }

        services.AddSingleton(new KafkaDeadLetterOptions
        {
            TopicName = topicName,
            DestinationAddress = BuildDestinationAddress(topicName),
        });
        services.AddSingleton<IDeadLetterSink, DlqProducer>();

        Topic<byte[], byte[]>(topicName, t =>
        {
            t.SetByteArrayKeySerializer();
            t.SetByteArrayValueSerializer();
            t.SetByteArrayKeyDeserializer();
            t.SetByteArrayValueDeserializer();
            configure?.Invoke(t);
        });

        return this;
    }

    /// <summary>
    /// Declares a topic with its key/value types, serializers, deserializers,
    /// and nested producer/consumer group configurations.
    /// </summary>
    /// <exception cref="InvalidOperationException">Duplicate topic name.</exception>
    public KafkaBuilder Topic<TKey, TValue>(
        string topicName,
        Action<KafkaTopicBuilder<TKey, TValue>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentNullException.ThrowIfNull(configure);

        if (!registeredTopicNames.Add(topicName))
        {
            throw new InvalidOperationException(
                $"A topic with name '{topicName}' has already been declared.");
        }

        var topicBuilder = new KafkaTopicBuilder<TKey, TValue>(topicName);
        configure(topicBuilder);
        EnsureSchemaRegistryConfigured(topicName, topicBuilder);

        if (topicBuilder.ProvisioningOptions is not null)
        {
            provisioningConfigs[topicName] = topicBuilder.ProvisioningOptions;
        }

        if (topicBuilder.ProducerConfigured)
        {
            ValidateSerializer(topicName, "key", topicBuilder.KeySerializer, topicBuilder.KeyAsyncSerializer, topicBuilder.KeyAsyncSerializerFactory);
            ValidateSerializer(topicName, "value", topicBuilder.ValueSerializer, topicBuilder.ValueAsyncSerializer, topicBuilder.ValueAsyncSerializerFactory);
            RegisterProducer(topicName, topicBuilder);
        }

        if (topicBuilder.ConsumerGroups.Count > 0)
        {
            ValidateDeserializer(topicName, "key", topicBuilder.KeyDeserializer, topicBuilder.KeyAsyncDeserializer, topicBuilder.KeyAsyncDeserializerFactory);
            ValidateDeserializer(topicName, "value", topicBuilder.ValueDeserializer, topicBuilder.ValueAsyncDeserializer, topicBuilder.ValueAsyncDeserializerFactory);

            foreach (var (groupId, groupBuilder) in topicBuilder.ConsumerGroups)
            {
                RegisterConsumerGroup(topicName, topicBuilder, groupId, groupBuilder);
            }
        }

        return this;
    }

    private static void ValidateSerializer(string topicName, string component, object? sync, object? async, object? factory)
    {
        if (sync is null && async is null && factory is null)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}': a {component} serializer is required when a producer is declared. Call SetKeySerializer/SetValueSerializer.");
        }
    }

    private static void ValidateDeserializer(string topicName, string component, object? sync, object? async, object? factory)
    {
        if (sync is null && async is null && factory is null)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}': a {component} deserializer is required when a consumer group is declared. Call SetKeyDeserializer/SetValueDeserializer.");
        }
    }

    private void RegisterProducer<TKey, TValue>(
        string topicName,
        KafkaTopicBuilder<TKey, TValue> topicBuilder)
    {
        // Capture serializer references for terminal closure
        var keySerializer = topicBuilder.KeySerializer;
        var valueSerializer = topicBuilder.ValueSerializer;
        var keyAsyncSerializer = topicBuilder.KeyAsyncSerializer;
        var valueAsyncSerializer = topicBuilder.ValueAsyncSerializer;
        var keyAsyncSerializerFactory = topicBuilder.KeyAsyncSerializerFactory;
        var valueAsyncSerializerFactory = topicBuilder.ValueAsyncSerializerFactory;

        // Register per-producer middleware with appropriate lifetimes
        var producerBuilder = topicBuilder.ProducerBuilder;
        producerBuilder?.Pipeline.RegisterServices(services);

        // Capture pipeline references for closure
        var kafkaOutbound = OutboundPipeline;
        var capturedGlobalOutbound = globalOutboundPipeline;
        var useDirect = producerBuilder?.DirectEnabled == true;
        var useOutbox = outboxEnabled && !useDirect;
        var producerPipeline = producerBuilder?.Pipeline;

        // Build transport URIs for the producer
        var capturedDestinationAddress = BuildDestinationAddress(topicName);
        var capturedHostAddress = BuildHostAddress();

        // Thread-safe lazy pipeline building (built once on first resolution)
        IMiddlewarePipeline<SendContext<TValue>>? builtPipeline = null;
        var buildLock = new object();

        services.AddScoped<IEventProducer<TKey, TValue>>(sp =>
        {
            if (builtPipeline is null)
            {
                lock (buildLock)
                {
                    if (builtPipeline is null)
                    {
                        // Resolve factory-based serializers (schema registry is singleton)
                        var resolvedKeyAsync = keyAsyncSerializer ?? keyAsyncSerializerFactory?.Invoke(
                            sp.GetRequiredService<ConfluentSchemaRegistry.ISchemaRegistryClient>());
                        var resolvedValueAsync = valueAsyncSerializer ?? valueAsyncSerializerFactory?.Invoke(
                            sp.GetRequiredService<ConfluentSchemaRegistry.ISchemaRegistryClient>());

                        var terminal = useOutbox
                            ? CreateOutboxTerminal(topicName, capturedDestinationAddress, keySerializer, valueSerializer, resolvedKeyAsync, resolvedValueAsync)
                            : CreateDirectTerminal(topicName, keySerializer, valueSerializer, resolvedKeyAsync, resolvedValueAsync);

                        // Pipeline layering: global → kafka → per-producer → terminal
                        builtPipeline = producerPipeline is not null
                            ? producerPipeline.Build<SendContext<TValue>, TValue>(
                                sp, terminal, capturedGlobalOutbound, kafkaOutbound)
                            : kafkaOutbound.Build<SendContext<TValue>, TValue>(
                                sp, terminal, capturedGlobalOutbound);
                    }
                }
            }

            return new KafkaPipelineProducer<TKey, TValue>(
                builtPipeline, capturedDestinationAddress, capturedHostAddress,
                sp, sp.GetRequiredService<TimeProvider>());
        });
    }

    private static async Task<(byte[]? KeyBytes, byte[]? ValueBytes)> SerializeMessageAsync<TKey, TValue>(
        TKey key,
        TValue value,
        string topicName,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        ConfluentKafka.ISerializer<TKey>? keySerializer,
        ConfluentKafka.ISerializer<TValue>? valueSerializer,
        ConfluentKafka.IAsyncSerializer<TKey>? keyAsyncSerializer,
        ConfluentKafka.IAsyncSerializer<TValue>? valueAsyncSerializer)
    {
        var kafkaHeaders = KafkaSerializationHelper.ConvertHeaders(headers);

        var keyBytes = await KafkaSerializationHelper.SerializeAsync(
            key, topicName, kafkaHeaders, keySerializer, keyAsyncSerializer,
            ConfluentKafka.MessageComponentType.Key).ConfigureAwait(false);
        var valueBytes = await KafkaSerializationHelper.SerializeAsync(
            value, topicName, kafkaHeaders, valueSerializer, valueAsyncSerializer,
            ConfluentKafka.MessageComponentType.Value).ConfigureAwait(false);

        return (keyBytes, valueBytes);
    }

    private static IMiddlewarePipeline<SendContext<TValue>> CreateOutboxTerminal<TKey, TValue>(
        string topicName,
        Uri destinationAddress,
        ConfluentKafka.ISerializer<TKey>? keySerializer,
        ConfluentKafka.ISerializer<TValue>? valueSerializer,
        ConfluentKafka.IAsyncSerializer<TKey>? keyAsyncSerializer,
        ConfluentKafka.IAsyncSerializer<TValue>? valueAsyncSerializer)
    {
        var destination = destinationAddress.ToString();

        return OutboxTerminalBuilder.Build<TValue>(async (context, ct) =>
        {
            var messageKey = context.TryGetPayload<KafkaTransportContext<TKey>>()!.Key;

            var (keyBytes, valueBytes) = await SerializeMessageAsync<TKey, TValue>(
                messageKey, context.Message, topicName, context.Headers, keySerializer, valueSerializer, keyAsyncSerializer, valueAsyncSerializer).ConfigureAwait(false);

            // Convert all context.Headers (user + trace) to byte headers for the outbox entry
            var headers = new List<KeyValuePair<string, byte[]>>();
            if (context.Headers is { Count: > 0 })
            {
                foreach (var (key, value) in context.Headers)
                    headers.Add(new(key, System.Text.Encoding.UTF8.GetBytes(value)));
            }

            // Build properties with provider metadata
            var properties = new Dictionary<string, string>
            {
                [OutboxPropertyKeys.Topic] = topicName,
                [OutboxPropertyKeys.KeyType] = typeof(TKey).FullName ?? typeof(TKey).Name,
                [OutboxPropertyKeys.ValueType] = typeof(TValue).FullName ?? typeof(TValue).Name,
            };

            // Store the serialized key as base64 in properties
            if (keyBytes is not null)
            {
                properties[OutboxPropertyKeys.Key] = Convert.ToBase64String(keyBytes);
            }

            var groupKey = keyBytes is not null
                ? $"{Provider.Identifier}:{topicName}:{Convert.ToBase64String(keyBytes)}"
                : $"{Provider.Identifier}:{topicName}";

            return new Models.OutboxEntry
            {
                SystemId = Provider.Identifier,
                Destination = destination,
                GroupKey = groupKey,
                Body = valueBytes,
                Headers = headers,
                Properties = properties,
            };
        });
    }

    private static IMiddlewarePipeline<SendContext<TValue>> CreateDirectTerminal<TKey, TValue>(
        string topicName,
        ConfluentKafka.ISerializer<TKey>? keySerializer,
        ConfluentKafka.ISerializer<TValue>? valueSerializer,
        ConfluentKafka.IAsyncSerializer<TKey>? keyAsyncSerializer,
        ConfluentKafka.IAsyncSerializer<TValue>? valueAsyncSerializer)
    {
        return new DirectTerminalPipeline<TKey, TValue>(
            topicName, keySerializer, valueSerializer, keyAsyncSerializer, valueAsyncSerializer);
    }

    private sealed class DirectTerminalPipeline<TKey, TValue>(
        string topicName,
        ConfluentKafka.ISerializer<TKey>? keySerializer,
        ConfluentKafka.ISerializer<TValue>? valueSerializer,
        ConfluentKafka.IAsyncSerializer<TKey>? keyAsyncSerializer,
        ConfluentKafka.IAsyncSerializer<TValue>? valueAsyncSerializer)
        : IMiddlewarePipeline<SendContext<TValue>>
    {
        public async Task InvokeAsync(SendContext<TValue> context)
        {
            var messageKey = context.TryGetPayload<KafkaTransportContext<TKey>>()!.Key;
            var confluentProducer = context.Services.GetRequiredService<ConfluentKafka.IProducer<byte[], byte[]>>();

            var (keyBytes, valueBytes) = await SerializeMessageAsync<TKey, TValue>(
                messageKey, context.Message, topicName, context.Headers, keySerializer, valueSerializer, keyAsyncSerializer, valueAsyncSerializer).ConfigureAwait(false);

            var kafkaMessage = new ConfluentKafka.Message<byte[], byte[]>
            {
                Key = keyBytes!,
                Value = valueBytes!,
            };

            // Convert all context.Headers (user + trace) to Kafka headers
            kafkaMessage.Headers = [];
            if (context.Headers is { Count: > 0 })
            {
                foreach (var (key, value) in context.Headers)
                    kafkaMessage.Headers.Add(key, System.Text.Encoding.UTF8.GetBytes(value));
            }

            await confluentProducer.ProduceAsync(topicName, kafkaMessage, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private void RegisterConsumerGroup<TKey, TValue>(
        string topicName,
        KafkaTopicBuilder<TKey, TValue> topicBuilder,
        string groupId,
        KafkaConsumerGroupBuilder<TKey, TValue> groupBuilder)
    {
        // Register each consumer type as scoped (idempotent)
        foreach (var consumerType in groupBuilder.ConsumerTypes)
        {
            services.TryAddScoped(consumerType);
        }

        // Register batch consumer + adapter in DI
        if (groupBuilder.IsBatchMode && groupBuilder.BatchConsumerType is not null)
        {
            var batchConsumerType = groupBuilder.BatchConsumerType;
            services.TryAddScoped(batchConsumerType);

            var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), batchConsumerType);
            services.TryAddScoped(adapterType);
        }

        // Register router sub-consumer types as scoped + their per-route middleware
        if (groupBuilder.Routers is { Count: > 0 })
        {
            foreach (var router in groupBuilder.Routers)
            {
                router.RegisterServices(services);
            }
        }

        // Register per-group middleware with appropriate lifetimes
        groupBuilder.Pipeline.RegisterServices(services);

        // Register per-consumer middleware with appropriate lifetimes
        foreach (var consumerPipeline in groupBuilder.ConsumerPipelines.Values)
        {
            consumerPipeline.RegisterServices(services);
        }

        // Capture factory references for the closure
        var keyDeserializerFactory = topicBuilder.KeyAsyncDeserializerFactory;
        var valueDeserializerFactory = topicBuilder.ValueAsyncDeserializerFactory;

        // Build invokers at registration time, paired with optional per-consumer pipelines
        var consumerPipelines = groupBuilder.ConsumerPipelines;
        var invokerEntries = groupBuilder.ConsumerTypes
            .Select(t => (
                ConsumerType: t,
                Invoker: (IMiddlewarePipeline<ConsumeContext<TValue>>)new HandlerInvoker<TValue>(t),
                Pipeline: consumerPipelines.GetValueOrDefault(t)))
            .ToList();

        // Build batch invoker entry if in batch mode
        (Type AdapterType, IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>> Invoker, Type UserConsumerType)? batchInvokerEntry = null;

        if (groupBuilder.IsBatchMode && groupBuilder.BatchConsumerType is not null)
        {
            var userType = groupBuilder.BatchConsumerType;
            var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), userType);
            var invoker = new HandlerInvoker<MessageBatch<TValue>>(adapterType);
            batchInvokerEntry = (adapterType, invoker, userType);
        }

        // Capture router registrations for the closure
        var routers = groupBuilder.Routers;

        // Capture build-time values for the closure
        var workerCount = groupBuilder.WorkerCount;
        var workerDistribution = groupBuilder.WorkerDistribution;
        var bufferSize = groupBuilder.BufferSize;
        var commitInterval = groupBuilder.CommitInterval;
        var workerStopTimeout = groupBuilder.WorkerStopTimeout;

        // Build transport URI for the consumer group
        var capturedDestinationAddress = BuildDestinationAddress(topicName);

        // Build error policies at registration time
        var groupErrorPolicy = BuildErrorPolicy(groupBuilder.GroupErrorPolicyAction);
        var deserializationErrorAction = BuildDeserializationErrorAction(groupBuilder.DeserializationErrorAction);
        var validationModule = groupBuilder.Validation;
        validationModule?.RegisterServices(services);

        // Extract retry config from error policy (retry is now a separate middleware concern)
        var retryConfig = ExtractRetryConfig(groupErrorPolicy);

        // Build rate limiter at registration time (singleton, shared across all workers).
        // In batch mode, the rate limiter is composed directly into the batch pipeline
        // (typed as MessageBatch<TValue>) rather than registered on groupBuilder.Pipeline
        // (typed as TValue), which would cause an invalid cast at pipeline build time.
        System.Threading.RateLimiting.RateLimiter? rateLimiter = null;
        if (groupBuilder.RateLimitAction is not null)
        {
            var rateLimitBuilder = new RateLimiting.RateLimitBuilder();
            groupBuilder.RateLimitAction(rateLimitBuilder);
            var totalCapacity = workerCount * bufferSize;
            rateLimiter = rateLimitBuilder.Build(totalCapacity);

            if (!groupBuilder.IsBatchMode)
            {
                groupBuilder.Pipeline.Use(
                    sp => new RateLimitMiddleware<TValue>(rateLimiter, sp.GetRequiredService<EmitMetrics>()),
                    Abstractions.Pipeline.MiddlewareLifetime.Singleton);
            }
        }

        // Build circuit breaker config at registration time (validated eagerly)
        CircuitBreakerConfig? circuitBreakerConfig = null;
        if (groupBuilder.CircuitBreakerAction is not null)
        {
            var cbBuilder = new CircuitBreakerBuilder();
            groupBuilder.CircuitBreakerAction(cbBuilder);
            circuitBreakerConfig = cbBuilder.Build();
        }

        // Capture pipeline builders for the closure
        var kafkaInbound = InboundPipeline;
        var groupPipeline = groupBuilder.Pipeline;
        var capturedBatchConfig = groupBuilder.BatchConfig;
        var capturedIsBatchMode = groupBuilder.IsBatchMode;

        // In batch mode, the rate limiter must be typed for MessageBatch<TValue> rather
        // than TValue. Since batch mode and AddConsumer are mutually exclusive, the TValue-
        // typed pipeline build never runs, so it's safe to add the batch-typed factory
        // directly to groupPipeline.
        if (capturedIsBatchMode && rateLimiter is not null)
        {
            groupPipeline.Use<ConsumeContext<MessageBatch<TValue>>>(
                sp => new RateLimitMiddleware<MessageBatch<TValue>>(rateLimiter, sp.GetRequiredService<EmitMetrics>()),
                Abstractions.Pipeline.MiddlewareLifetime.Singleton);
        }

        // Register the hosted service — registration is built inside the factory
        // so that deferred deserializer factories can be resolved via the service provider.
        services.AddSingleton<IHostedService>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // Create flow control first — shared between the circuit breaker and the worker
            var flowControl = new KafkaConsumerFlowControl(
                loggerFactory.CreateLogger<KafkaConsumerFlowControl>());

            // Create circuit breaker observer if configured (shared across all workers)
            CircuitBreakerObserver<TValue>? cbObserver = null;
            if (circuitBreakerConfig is not null)
            {
                cbObserver = new CircuitBreakerObserver<TValue>(
                    circuitBreakerConfig,
                    flowControl,
                    sp.GetRequiredService<EmitMetrics>(),
                    loggerFactory.CreateLogger<CircuitBreakerObserver<TValue>>());
            }

            // Resolve observers once for error handling middleware (shared across all consumers)
            var consumeObservers = sp.GetServices<IConsumeObserver>().ToList();

            var registration = new ConsumerGroupRegistration<TKey, TValue>
            {
                TopicName = topicName,
                GroupId = groupId,
                DestinationAddress = capturedDestinationAddress,
                KeyDeserializer = topicBuilder.KeyDeserializer,
                ValueDeserializer = topicBuilder.ValueDeserializer,
                KeyAsyncDeserializer = topicBuilder.KeyAsyncDeserializer ?? keyDeserializerFactory?.Invoke(sp.GetRequiredService<ConfluentSchemaRegistry.ISchemaRegistryClient>()),
                ValueAsyncDeserializer = topicBuilder.ValueAsyncDeserializer ?? valueDeserializerFactory?.Invoke(sp.GetRequiredService<ConfluentSchemaRegistry.ISchemaRegistryClient>()),
                // Pipeline factory — called per worker for per-worker middleware isolation.
                // Singleton middleware is shared (same DI singleton); factory middleware creates fresh instances per build.
                BuildConsumerPipelines = () =>
                {
                    // Build error evaluator that strips RetryAction (retry handled by RetryMiddleware)
                    Func<Exception, ErrorAction>? errorEvaluator = groupErrorPolicy is not null
                        ? ex => StripRetryAction(groupErrorPolicy.Evaluate(ex))
                        : null;

                    if (validationModule is { ValidationErrorAction: not null } && !capturedIsBatchMode)
                    {
                        var validationErrorAction = validationModule.ValidationErrorAction;
                        var innerEvaluator = errorEvaluator;
                        errorEvaluator = ex => ex is MessageValidationException
                            ? validationErrorAction
                            : innerEvaluator is not null
                                ? innerEvaluator(ex)
                                : ErrorAction.Discard();
                    }

                    var composer = new ConsumerPipelineComposer<TValue>
                    {
                        Services = sp,
                        LoggerFactory = loggerFactory,
                        Validation = validationModule,
                        RetryConfig = retryConfig,
                        ErrorPolicy = errorEvaluator,
                        ConsumeObservers = consumeObservers,
                        GroupPipeline = groupPipeline,
                        GlobalInboundPipeline = globalInboundPipeline,
                        ProviderInboundPipeline = kafkaInbound,
                        CircuitBreakerNotifier = cbObserver,
                        OutboxEnabled = outboxEnabled,
                    };

                    var entries = new List<ConsumerPipelineEntry<TValue>>();

                    foreach (var entry in invokerEntries)
                    {
                        entries.Add(composer.Compose(
                            entry.Invoker, entry.Pipeline,
                            entry.ConsumerType.Name, ConsumerKind.Direct, entry.ConsumerType));
                    }

                    if (routers is { Count: > 0 })
                    {
                        foreach (var routerReg in routers)
                        {
                            // Strip retry from unmatched route action (router only checks for DiscardAction)
                            var unmatchedAction = groupErrorPolicy is not null
                                ? StripRetryAction(groupErrorPolicy.Evaluate(new UnmatchedRouteException(null)))
                                : null;

                            var routerInvoker = routerReg.BuildInvoker(
                                type => new HandlerInvoker<TValue>(type),
                                sp,
                                loggerFactory,
                                unmatchedAction,
                                outboxEnabled);

                            entries.Add(composer.Compose(
                                routerInvoker, null,
                                routerReg.Identifier, ConsumerKind.Router, null));
                        }
                    }

                    return entries;
                },
                WorkerCount = workerCount,
                WorkerDistribution = workerDistribution,
                BufferSize = bufferSize,
                CommitInterval = commitInterval,
                WorkerStopTimeout = workerStopTimeout,
                ApplyClientConfig = config => ApplyClientConfigTo(config),
                ApplyConsumerConfigOverrides = config => groupBuilder.ApplyTo(config),
                GroupErrorPolicy = groupErrorPolicy,
                DeserializationErrorAction = deserializationErrorAction,
                ConsumerTypes = invokerEntries.Select(e => e.ConsumerType)
                    .Concat(routers?.SelectMany(r => r.ConsumerTypes) ?? [])
                    .ToList(),
                CircuitBreakerEnabled = circuitBreakerConfig is not null,
                RateLimitEnabled = groupBuilder.RateLimitAction is not null,
                BatchConfig = capturedBatchConfig,
                BuildBatchConsumerPipelines = batchInvokerEntry.HasValue
                    ? () =>
                    {
                        var (adapterType, invoker, userType) = batchInvokerEntry.Value;

                        IMiddleware<ConsumeContext<MessageBatch<TValue>>>? preBuiltBatchValidation = null;

                        if (validationModule is { IsConfigured: true })
                        {
                            preBuiltBatchValidation = new BatchValidationMiddleware<TValue>(
                                validationModule,
                                validationModule.ValidationErrorAction ?? ErrorAction.Discard(),
                                sp.GetService<IDeadLetterSink>(),
                                sp.GetRequiredService<EmitMetrics>(),
                                loggerFactory.CreateLogger<BatchValidationMiddleware<TValue>>());
                        }

                        Func<Exception, ErrorAction>? batchErrorEvaluator = groupErrorPolicy is not null
                            ? ex => StripRetryAction(groupErrorPolicy.Evaluate(ex))
                            : null;

                        var batchComposer = new ConsumerPipelineComposer<MessageBatch<TValue>>
                        {
                            Services = sp,
                            LoggerFactory = loggerFactory,
                            Validation = null,
                            PreBuiltValidationMiddleware = preBuiltBatchValidation,
                            RetryConfig = retryConfig,
                            ErrorPolicy = batchErrorEvaluator,
                            ConsumeObservers = consumeObservers,
                            GroupPipeline = groupPipeline,
                            GlobalInboundPipeline = globalInboundPipeline,
                            ProviderInboundPipeline = kafkaInbound,
                            CircuitBreakerNotifier = cbObserver,
                            OutboxEnabled = outboxEnabled,
                        };

                        var batchEntry = batchComposer.Compose(
                            invoker, null,
                            userType.Name, ConsumerKind.Direct, userType);

                        return [batchEntry];
                    }
                : null,
            };

            return new ConsumerGroupWorker<TKey, TValue>(
                registration,
                flowControl,
                cbObserver,
                sp.GetRequiredService<IServiceScopeFactory>(),
                loggerFactory,
                sp.GetRequiredService<KafkaConsumerObserverInvoker>(),
                sp.GetRequiredService<KafkaMetrics>(),
                sp.GetRequiredService<KafkaBrokerMetrics>(),
                sp.GetRequiredService<EmitMetrics>(),
                sp.GetService<IDeadLetterSink>());
        });
    }

    private void ApplyClientConfigTo(ConfluentKafka.ClientConfig config)
    {
        if (ClientConfigAction is not null)
        {
            var kafkaClientConfig = new KafkaClientConfig();
            ClientConfigAction(kafkaClientConfig);
            kafkaClientConfig.ApplyTo(config);
        }
    }

    /// <summary>
    /// Resolves the bootstrap servers string from the client config action.
    /// Falls back to <c>localhost:9092</c> if not configured.
    /// </summary>
    private string ResolveBootstrapServers()
    {
        if (ClientConfigAction is null)
        {
            return "localhost:9092";
        }

        var config = new KafkaClientConfig();
        ClientConfigAction(config);
        return config.BootstrapServers ?? "localhost:9092";
    }

    /// <summary>
    /// Builds a destination <see cref="Uri"/> for a Kafka topic using the configured broker address.
    /// </summary>
    private Uri BuildDestinationAddress(string topicName)
    {
        var (host, port) = ParseBrokerAddress(ResolveBootstrapServers());
        return EmitEndpointAddress.ForEntity("kafka", host, port, topicName);
    }

    /// <summary>
    /// Builds a host-only <see cref="Uri"/> for the configured Kafka broker (used as SourceAddress).
    /// </summary>
    private Uri BuildHostAddress()
    {
        var (host, port) = ParseBrokerAddress(ResolveBootstrapServers());
        return EmitEndpointAddress.ForHost("kafka", host, port);
    }

    private static (string Host, int? Port) ParseBrokerAddress(string bootstrapServers)
    {
        // Take first broker from CSV list
        var firstBroker = bootstrapServers.Split(',')[0].Trim();

        // Strip protocol prefix if present (e.g. "PLAINTEXT://host:port")
        var schemeIndex = firstBroker.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            firstBroker = firstBroker[(schemeIndex + 3)..];
        }

        // Strip trailing slash
        firstBroker = firstBroker.TrimEnd('/');

        var colonIndex = firstBroker.LastIndexOf(':');

        if (colonIndex > 0 && int.TryParse(firstBroker[(colonIndex + 1)..], out var port))
        {
            return (firstBroker[..colonIndex], port);
        }

        return (firstBroker, null);
    }

    private void EnsureSchemaRegistryConfigured<TKey, TValue>(
        string topicName,
        KafkaTopicBuilder<TKey, TValue> topicBuilder)
    {
        bool hasFactory = topicBuilder.KeyAsyncSerializerFactory is not null
            || topicBuilder.ValueAsyncSerializerFactory is not null
            || topicBuilder.KeyAsyncDeserializerFactory is not null
            || topicBuilder.ValueAsyncDeserializerFactory is not null;

        if (hasFactory && !services.Any(d => d.ServiceType == typeof(SchemaRegistryMarker)))
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}': a schema-registry-dependent serializer factory was configured, but {nameof(ConfigureSchemaRegistry)} has not been called.");
        }
    }

    private void RegisterSchemaRegistryClient(Action<KafkaSchemaRegistryConfig> configure)
    {
        services.AddSingleton<ConfluentSchemaRegistry.ISchemaRegistryClient>(_ =>
        {
            var schemaRegistryConfig = new KafkaSchemaRegistryConfig();
            configure(schemaRegistryConfig);

            var confluentConfig = new ConfluentSchemaRegistry.SchemaRegistryConfig();
            schemaRegistryConfig.ApplyTo(confluentConfig);

            return new ConfluentSchemaRegistry.CachedSchemaRegistryClient(confluentConfig);
        });
    }

    private static ErrorPolicy? BuildErrorPolicy(Action<ErrorPolicyBuilder>? configureAction)
    {
        if (configureAction is null)
        {
            return null;
        }

        var builder = new ErrorPolicyBuilder();
        configureAction(builder);
        return builder.Build();
    }

    private static ErrorAction? BuildDeserializationErrorAction(
        Action<ErrorActionBuilder>? configureAction)
    {
        if (configureAction is null)
        {
            return null;
        }

        var builder = new ErrorActionBuilder();
        configureAction(builder);
        return builder.Build();
    }

    /// <summary>
    /// Returns all registered topic names (including the DLQ topic, if configured).
    /// </summary>
    internal IReadOnlySet<string> GetRequiredTopics() => registeredTopicNames;

    /// <summary>
    /// Returns per-topic provisioning configurations collected from <see cref="KafkaTopicBuilder{TKey,TValue}.Provisioning"/> calls.
    /// </summary>
    internal IReadOnlyDictionary<string, TopicCreationOptions> GetProvisioningConfigs() => provisioningConfigs;

    /// <summary>
    /// Extracts retry configuration from the default action of an error policy.
    /// Returns <c>null</c> if the default action is not a <see cref="ErrorAction.RetryAction"/>.
    /// </summary>
    private static RetryConfig? ExtractRetryConfig(ErrorPolicy? policy)
    {
        if (policy?.DefaultAction is ErrorAction.RetryAction retry)
        {
            return new RetryConfig(retry.MaxAttempts, retry.Backoff);
        }

        return null;
    }

    /// <summary>
    /// Strips <see cref="ErrorAction.RetryAction"/> wrapping from an error action,
    /// returning the exhaustion action. Retry is handled by <c>RetryMiddleware</c>;
    /// the error policy should only return dead-letter or discard.
    /// </summary>
    private static ErrorAction StripRetryAction(ErrorAction action) =>
        action is ErrorAction.RetryAction retry ? retry.ExhaustionAction : action;

    private sealed class SchemaRegistryMarker;
}
