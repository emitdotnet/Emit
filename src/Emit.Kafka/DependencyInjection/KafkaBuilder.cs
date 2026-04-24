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
public sealed class KafkaBuilder : IInboundConfigurable, IOutboundConfigurable
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
    internal Action<KafkaClientOptions>? ClientConfigAction { get; private set; }

    /// <summary>
    /// The stored producer configuration action. Applied to the shared producer config.
    /// </summary>
    internal Action<KafkaProducerOptions>? ProducerConfigAction { get; private set; }

    /// <summary>
    /// The stored schema registry configuration action.
    /// </summary>
    internal Action<KafkaSchemaRegistryOptions>? SchemaRegistryConfigAction { get; private set; }

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
    public KafkaBuilder ConfigureClient(Action<KafkaClientOptions> configure)
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
    public KafkaBuilder ConfigureProducer(Action<KafkaProducerOptions> configure)
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
    public KafkaBuilder ConfigureSchemaRegistry(Action<KafkaSchemaRegistryOptions> configure)
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
        var kafkaHeaders = KafkaSerializer.ConvertHeaders(headers);

        var keyBytes = await KafkaSerializer.SerializeAsync(
            key, topicName, kafkaHeaders, keySerializer, keyAsyncSerializer,
            ConfluentKafka.MessageComponentType.Key).ConfigureAwait(false);
        var valueBytes = await KafkaSerializer.SerializeAsync(
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
            var kafkaMetrics = context.Services.GetRequiredService<Metrics.KafkaMetrics>();

            var (keyBytes, valueBytes) = await SerializeMessageAsync<TKey, TValue>(
                messageKey, context.Message, topicName, context.Headers, keySerializer, valueSerializer, keyAsyncSerializer, valueAsyncSerializer).ConfigureAwait(false);

            var messageSize = (keyBytes?.Length ?? 0) + (valueBytes?.Length ?? 0);
            kafkaMetrics.RecordProduceMessageSize(messageSize, topicName);

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

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                await confluentProducer.ProduceAsync(topicName, kafkaMessage, context.CancellationToken).ConfigureAwait(false);

                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds;
                kafkaMetrics.RecordProduceDuration(elapsed, topicName, "success");
                kafkaMetrics.RecordProduceMessage(topicName, "success");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds;
                kafkaMetrics.RecordProduceDuration(elapsed, topicName, "error");
                kafkaMetrics.RecordProduceMessage(topicName, "error");
                throw;
            }
        }
    }

    private void RegisterConsumerGroup<TKey, TValue>(
        string topicName,
        KafkaTopicBuilder<TKey, TValue> topicBuilder,
        string groupId,
        KafkaConsumerGroupBuilder<TKey, TValue> groupBuilder)
    {
        RegisterConsumerTypes(groupBuilder);
        RegisterMiddleware(groupBuilder);

        var config = BuildConsumerGroupConfig(topicName, topicBuilder, groupBuilder);

        services.AddSingleton<IHostedService>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // Create flow control first — shared between the circuit breaker and the worker
            var flowControl = new KafkaConsumerFlowControl(
                loggerFactory.CreateLogger<KafkaConsumerFlowControl>());

            // Create circuit breaker observer if configured (shared across all workers)
            CircuitBreakerObserver<TValue>? cbObserver = null;
            if (config.CircuitBreakerConfig is not null)
            {
                cbObserver = new CircuitBreakerObserver<TValue>(
                    config.CircuitBreakerConfig,
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
                DestinationAddress = config.DestinationAddress,
                KeyDeserializer = topicBuilder.KeyDeserializer,
                ValueDeserializer = topicBuilder.ValueDeserializer,
                KeyAsyncDeserializer = topicBuilder.KeyAsyncDeserializer ?? config.KeyDeserializerFactory?.Invoke(sp.GetRequiredService<ConfluentSchemaRegistry.ISchemaRegistryClient>()),
                ValueAsyncDeserializer = topicBuilder.ValueAsyncDeserializer ?? config.ValueDeserializerFactory?.Invoke(sp.GetRequiredService<ConfluentSchemaRegistry.ISchemaRegistryClient>()),
                // Pipeline factory — called per worker for per-worker middleware isolation.
                // Singleton middleware is shared (same DI singleton); factory middleware creates fresh instances per build.
                BuildConsumerPipelines = () =>
                {
                    // Build error evaluator that strips RetryAction (retry handled by RetryMiddleware)
                    Func<Exception, ErrorAction>? errorEvaluator = config.GroupErrorPolicy is not null
                        ? ex => StripRetryAction(config.GroupErrorPolicy.Evaluate(ex))
                        : null;

                    if (config.ValidationModule is { ValidationErrorAction: not null } && !config.IsBatchMode)
                    {
                        var validationErrorAction = config.ValidationModule.ValidationErrorAction;
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
                        Validation = config.ValidationModule,
                        RetryConfig = config.RetryConfig,
                        ErrorPolicy = errorEvaluator,
                        ConsumeObservers = consumeObservers,
                        GroupPipeline = config.GroupPipeline,
                        GlobalInboundPipeline = globalInboundPipeline,
                        ProviderInboundPipeline = config.KafkaInboundPipeline,
                        CircuitBreakerNotifier = cbObserver,
                        OutboxEnabled = outboxEnabled,
                    };

                    var entries = new List<ConsumerPipelineEntry<TValue>>();

                    foreach (var entry in config.InvokerEntries)
                    {
                        entries.Add(composer.Compose(
                            entry.Invoker, entry.Pipeline,
                            entry.ConsumerType.Name, ConsumerKind.Direct, entry.ConsumerType));
                    }

                    if (config.Routers is { Count: > 0 })
                    {
                        foreach (var routerReg in config.Routers)
                        {
                            // Strip retry from unmatched route action (router only checks for DiscardAction)
                            var unmatchedAction = config.GroupErrorPolicy is not null
                                ? StripRetryAction(config.GroupErrorPolicy.Evaluate(new UnmatchedRouteException(null)))
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
                WorkerCount = config.WorkerCount,
                WorkerDistribution = config.WorkerDistribution,
                BufferSize = config.BufferSize,
                CommitInterval = config.CommitInterval,
                WorkerStopTimeout = config.WorkerStopTimeout,
                ApplyClientConfig = clientConfig => ApplyClientConfigTo(clientConfig),
                ApplyConsumerConfigOverrides = config.ApplyConsumerConfigOverrides,
                GroupErrorPolicy = config.GroupErrorPolicy,
                DeserializationErrorAction = config.DeserializationErrorAction,
                ConsumerTypes = config.InvokerEntries.Select(e => e.ConsumerType)
                    .Concat(config.Routers?.SelectMany(r => r.ConsumerTypes) ?? [])
                    .ToList(),
                CircuitBreakerEnabled = config.CircuitBreakerConfig is not null,
                RateLimitEnabled = config.RateLimitEnabled,
                BatchOptions = config.BatchOptions,
                BuildBatchConsumerPipelines = config.BatchInvokerEntry.HasValue
                    ? () =>
                    {
                        var (adapterType, invoker, userType) = config.BatchInvokerEntry.Value;

                        IMiddleware<ConsumeContext<MessageBatch<TValue>>>? preBuiltBatchValidation = null;

                        if (config.ValidationModule is { IsConfigured: true })
                        {
                            preBuiltBatchValidation = new BatchValidationMiddleware<TValue>(
                                config.ValidationModule,
                                config.ValidationModule.ValidationErrorAction ?? ErrorAction.Discard(),
                                sp.GetService<IDeadLetterSink>(),
                                sp.GetRequiredService<EmitMetrics>(),
                                loggerFactory.CreateLogger<BatchValidationMiddleware<TValue>>());
                        }

                        Func<Exception, ErrorAction>? batchErrorEvaluator = config.GroupErrorPolicy is not null
                            ? ex => StripRetryAction(config.GroupErrorPolicy.Evaluate(ex))
                            : null;

                        var batchComposer = new ConsumerPipelineComposer<MessageBatch<TValue>>
                        {
                            Services = sp,
                            LoggerFactory = loggerFactory,
                            Validation = null,
                            PreBuiltValidationMiddleware = preBuiltBatchValidation,
                            RetryConfig = config.RetryConfig,
                            ErrorPolicy = batchErrorEvaluator,
                            ConsumeObservers = consumeObservers,
                            GroupPipeline = config.GroupPipeline,
                            GlobalInboundPipeline = globalInboundPipeline,
                            ProviderInboundPipeline = config.KafkaInboundPipeline,
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

    private void RegisterConsumerTypes<TKey, TValue>(KafkaConsumerGroupBuilder<TKey, TValue> groupBuilder)
    {
        foreach (var consumerType in groupBuilder.ConsumerTypes)
        {
            services.TryAddScoped(consumerType);
        }

        if (groupBuilder.IsBatchMode && groupBuilder.BatchConsumerType is not null)
        {
            var batchConsumerType = groupBuilder.BatchConsumerType;
            services.TryAddScoped(batchConsumerType);

            var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), batchConsumerType);
            services.TryAddScoped(adapterType);
        }
    }

    private void RegisterMiddleware<TKey, TValue>(KafkaConsumerGroupBuilder<TKey, TValue> groupBuilder)
    {
        if (groupBuilder.Routers is { Count: > 0 })
        {
            foreach (var router in groupBuilder.Routers)
            {
                router.RegisterServices(services);
            }
        }

        groupBuilder.Pipeline.RegisterServices(services);

        foreach (var consumerPipeline in groupBuilder.ConsumerPipelines.Values)
        {
            consumerPipeline.RegisterServices(services);
        }
    }

    private ConsumerGroupConfig<TKey, TValue> BuildConsumerGroupConfig<TKey, TValue>(
        string topicName,
        KafkaTopicBuilder<TKey, TValue> topicBuilder,
        KafkaConsumerGroupBuilder<TKey, TValue> groupBuilder)
    {
        var keyDeserializerFactory = topicBuilder.KeyAsyncDeserializerFactory;
        var valueDeserializerFactory = topicBuilder.ValueAsyncDeserializerFactory;

        var consumerPipelines = groupBuilder.ConsumerPipelines;
        var invokerEntries = groupBuilder.ConsumerTypes
            .Select(t => (
                ConsumerType: t,
                Invoker: (IMiddlewarePipeline<ConsumeContext<TValue>>)new HandlerInvoker<TValue>(t),
                Pipeline: consumerPipelines.GetValueOrDefault(t)))
            .ToList();

        (Type AdapterType, IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>> Invoker, Type UserConsumerType)? batchInvokerEntry = null;

        if (groupBuilder.IsBatchMode && groupBuilder.BatchConsumerType is not null)
        {
            var userType = groupBuilder.BatchConsumerType;
            var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), userType);
            var invoker = new HandlerInvoker<MessageBatch<TValue>>(adapterType);
            batchInvokerEntry = (adapterType, invoker, userType);
        }

        var routers = groupBuilder.Routers;

        var workerCount = groupBuilder.WorkerCount;
        var workerDistribution = groupBuilder.WorkerDistribution;
        var bufferSize = groupBuilder.BufferSize;
        var commitInterval = groupBuilder.CommitInterval;
        var workerStopTimeout = groupBuilder.WorkerStopTimeout;

        var destinationAddress = BuildDestinationAddress(topicName);

        var groupErrorPolicy = BuildErrorPolicy(groupBuilder.GroupErrorPolicyAction);
        var deserializationErrorAction = BuildDeserializationErrorAction(groupBuilder.DeserializationErrorAction);
        var validationModule = groupBuilder.Validation;
        validationModule?.RegisterServices(services);

        var retryConfig = ExtractRetryConfig(groupErrorPolicy);

        System.Threading.RateLimiting.RateLimiter? rateLimiter = null;
        if (groupBuilder.RateLimitAction is not null)
        {
            var rateLimitBuilder = new RateLimiting.RateLimitBuilder();
            groupBuilder.RateLimitAction(rateLimitBuilder);
            var totalCapacity = groupBuilder.IsBatchMode
                ? workerCount * groupBuilder.BatchOptions!.MaxSize
                : workerCount * bufferSize;
            rateLimiter = rateLimitBuilder.Build(totalCapacity);

            if (!groupBuilder.IsBatchMode)
            {
                groupBuilder.Pipeline.Use(
                    sp => new RateLimitMiddleware<TValue>(rateLimiter, sp.GetRequiredService<EmitMetrics>()),
                    Abstractions.Pipeline.MiddlewareLifetime.Singleton);
            }
        }

        CircuitBreakerConfig? circuitBreakerConfig = null;
        if (groupBuilder.CircuitBreakerAction is not null)
        {
            var cbBuilder = new CircuitBreakerBuilder();
            groupBuilder.CircuitBreakerAction(cbBuilder);
            circuitBreakerConfig = cbBuilder.Build();
        }

        var kafkaInbound = InboundPipeline;
        var groupPipeline = groupBuilder.Pipeline;
        var batchOptions = groupBuilder.BatchOptions;
        var isBatchMode = groupBuilder.IsBatchMode;

        // In batch mode, the rate limiter must be typed for MessageBatch<TValue> rather
        // than TValue. Since batch mode and AddConsumer are mutually exclusive, the TValue-
        // typed pipeline build never runs, so it's safe to add the batch-typed factory
        // directly to groupPipeline.
        if (isBatchMode && rateLimiter is not null)
        {
            groupPipeline.Use<ConsumeContext<MessageBatch<TValue>>>(
                sp => new RateLimitMiddleware<MessageBatch<TValue>>(rateLimiter, sp.GetRequiredService<EmitMetrics>()),
                Abstractions.Pipeline.MiddlewareLifetime.Singleton);
        }

        return new ConsumerGroupConfig<TKey, TValue>
        {
            KeyDeserializerFactory = keyDeserializerFactory,
            ValueDeserializerFactory = valueDeserializerFactory,
            InvokerEntries = invokerEntries,
            BatchInvokerEntry = batchInvokerEntry,
            Routers = routers,
            WorkerCount = workerCount,
            WorkerDistribution = workerDistribution,
            BufferSize = bufferSize,
            CommitInterval = commitInterval,
            WorkerStopTimeout = workerStopTimeout,
            DestinationAddress = destinationAddress,
            GroupErrorPolicy = groupErrorPolicy,
            DeserializationErrorAction = deserializationErrorAction,
            ValidationModule = validationModule,
            RetryConfig = retryConfig,
            RateLimitEnabled = groupBuilder.RateLimitAction is not null,
            CircuitBreakerConfig = circuitBreakerConfig,
            KafkaInboundPipeline = kafkaInbound,
            GroupPipeline = groupPipeline,
            BatchOptions = batchOptions,
            IsBatchMode = isBatchMode,
            ApplyConsumerConfigOverrides = config => groupBuilder.ApplyTo(config),
        };
    }

    private sealed class ConsumerGroupConfig<TKey, TValue>
    {
        public required Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncDeserializer<TKey>>? KeyDeserializerFactory { get; init; }
        public required Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncDeserializer<TValue>>? ValueDeserializerFactory { get; init; }
        public required List<(Type ConsumerType, IMiddlewarePipeline<ConsumeContext<TValue>> Invoker, IMessagePipelineBuilder? Pipeline)> InvokerEntries { get; init; }
        public required (Type AdapterType, IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>> Invoker, Type UserConsumerType)? BatchInvokerEntry { get; init; }
        public required List<RouterRegistration<TValue>>? Routers { get; init; }
        public required int WorkerCount { get; init; }
        public required WorkerDistribution WorkerDistribution { get; init; }
        public required int BufferSize { get; init; }
        public required TimeSpan CommitInterval { get; init; }
        public required TimeSpan WorkerStopTimeout { get; init; }
        public required Uri DestinationAddress { get; init; }
        public required ErrorPolicy? GroupErrorPolicy { get; init; }
        public required ErrorAction? DeserializationErrorAction { get; init; }
        public required ValidationModule<TValue>? ValidationModule { get; init; }
        public required RetryConfig? RetryConfig { get; init; }
        public required bool RateLimitEnabled { get; init; }
        public required CircuitBreakerConfig? CircuitBreakerConfig { get; init; }
        public required IMessagePipelineBuilder KafkaInboundPipeline { get; init; }
        public required IMessagePipelineBuilder GroupPipeline { get; init; }
        public required BatchOptions? BatchOptions { get; init; }
        public required bool IsBatchMode { get; init; }
        public required Action<ConfluentKafka.ConsumerConfig> ApplyConsumerConfigOverrides { get; init; }
    }

    private void ApplyClientConfigTo(ConfluentKafka.ClientConfig config)
    {
        if (ClientConfigAction is not null)
        {
            var kafkaClientConfig = new KafkaClientOptions();
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

        var config = new KafkaClientOptions();
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

    private void RegisterSchemaRegistryClient(Action<KafkaSchemaRegistryOptions> configure)
    {
        services.AddSingleton<ConfluentSchemaRegistry.ISchemaRegistryClient>(_ =>
        {
            var schemaRegistryConfig = new KafkaSchemaRegistryOptions();
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
