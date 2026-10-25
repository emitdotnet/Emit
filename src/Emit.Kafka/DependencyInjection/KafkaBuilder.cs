namespace Emit.Kafka.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Observability;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Kafka.Consumer;
using Emit.Kafka.Metrics;
using Emit.Kafka.Observability;
using Emit.Kafka.Tracing;
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
using Microsoft.Extensions.Options;
using ConfluentKafka = Confluent.Kafka;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

/// <summary>
/// Configures the Kafka provider within an <see cref="Emit.DependencyInjection.EmitBuilder"/>.
/// Provides shared client configuration and topic declarations.
/// </summary>
public sealed class KafkaBuilder
{
    private readonly IServiceCollection services;
    private readonly MessagePipelineBuilder globalPipeline;
    private readonly MessagePipelineBuilder globalInboundPipeline;
    private readonly MessagePipelineBuilder globalOutboundPipeline;
    private readonly HashSet<string> registeredTopicNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TopicCreationOptions> provisioningConfigs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Topic, string GroupId)> consumerGroupNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the Kafka middleware level of every message consumed: it wraps all Kafka consumer handlers across all
    /// consumer groups.
    /// </summary>
    public IMessagePipelineBuilder<ConsumeContext> InboundPipeline => InboundLevel;

    /// <summary>
    /// Gets the Kafka middleware level of every message produced: it wraps all Kafka producers across all topics.
    /// </summary>
    public IMessagePipelineBuilder<SendContext> OutboundPipeline => OutboundLevel;

    /// <summary>
    /// The level behind <see cref="InboundPipeline"/>.
    /// </summary>
    internal MessagePipelineBuilder<ConsumeContext> InboundLevel { get; } = new(typeof(ConsumeContext<>));

    /// <summary>
    /// The level behind <see cref="OutboundPipeline"/>.
    /// </summary>
    internal MessagePipelineBuilder<SendContext> OutboundLevel { get; } = new(typeof(SendContext<>));

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
    internal KafkaBuilder(EmitBuilder emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        services = emit.Services;
        globalPipeline = emit.PipelineLevel;
        globalInboundPipeline = emit.InboundLevel;
        globalOutboundPipeline = emit.OutboundLevel;
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
        var kafkaOutbound = OutboundLevel;
        var capturedGlobal = globalPipeline;
        var capturedGlobalOutbound = globalOutboundPipeline;
        var useDirect = producerBuilder?.DirectEnabled == true;
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

                        // Routing is decided here rather than during registration. A snapshot
                        // taken while services were still being registered would send messages
                        // directly whenever the persistence provider happened to be registered
                        // after AddKafka, silently breaking the atomicity the outbox provides.
                        var useOutbox = !useDirect && sp.IsOutboxEnabled();

                        var terminal = useOutbox
                            ? CreateOutboxTerminal(topicName, capturedDestinationAddress, keySerializer, valueSerializer, resolvedKeyAsync, resolvedValueAsync)
                            : CreateDirectTerminal(topicName, keySerializer, valueSerializer, resolvedKeyAsync, resolvedValueAsync);

                        // Pipeline layering: built-ins → every pipeline → global outbound → kafka → per-producer
                        // → terminal
                        builtPipeline = ProducerPipelineComposer.Compose(
                            sp, terminal, capturedGlobal, capturedGlobalOutbound, kafkaOutbound, producerPipeline);
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
            var messageKey = context.TryGetPayload<KafkaMessageKey<TKey>>()!.Key;

            var (keyBytes, valueBytes) = await SerializeMessageAsync<TKey, TValue>(
                messageKey, context.Message, topicName, context.Headers, keySerializer, valueSerializer, keyAsyncSerializer, valueAsyncSerializer).ConfigureAwait(false);

            return CreateOutboxEntry(destination, topicName, typeof(TKey), typeof(TValue), keyBytes, valueBytes, context.Headers);
        });
    }

    /// <summary>
    /// Creates the outbox entry of a serialized Kafka message. Messages with the same key on the same topic share
    /// a group, so they are delivered in order.
    /// </summary>
    internal static Models.OutboxEntry CreateOutboxEntry(
        string destination,
        string topicName,
        Type keyType,
        Type valueType,
        byte[]? keyBytes,
        byte[]? valueBytes,
        IEnumerable<KeyValuePair<string, string>>? headers)
    {
        // Convert all headers (user + trace) to byte headers for the outbox entry
        var byteHeaders = new List<KeyValuePair<string, byte[]>>();
        foreach (var (key, value) in headers ?? [])
        {
            byteHeaders.Add(new(key, System.Text.Encoding.UTF8.GetBytes(value)));
        }

        // Build properties with provider metadata
        var properties = new Dictionary<string, string>
        {
            [OutboxPropertyKeys.Topic] = topicName,
            [OutboxPropertyKeys.KeyType] = keyType.FullName ?? keyType.Name,
            [OutboxPropertyKeys.ValueType] = valueType.FullName ?? valueType.Name,
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
            Headers = byteHeaders,
            Properties = properties,
        };
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
            var messageKey = context.TryGetPayload<KafkaMessageKey<TKey>>()!.Key;
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

            // A child of the message's create span
            using var activity = KafkaSendActivity.Start(
                EmitActivitySources.Producer,
                context.Services.GetRequiredService<IOptions<EmitTracingOptions>>().Value,
                System.Diagnostics.ActivityKind.Client,
                context.DestinationAddress!,
                context.MessageId,
                context.Message is null,
                tags: [new(ActivityTagNames.NodeId, context.Services.GetRequiredService<INodeIdentity>().NodeId.ToString())]);

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var result = await confluentProducer.ProduceAsync(topicName, kafkaMessage, context.CancellationToken).ConfigureAwait(false);
                KafkaSendActivity.Complete(activity, result.Partition, result.Offset);

                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds;
                kafkaMetrics.RecordProduceDuration(elapsed, topicName, KafkaMetrics.ResultSuccess);
                kafkaMetrics.RecordProduceMessage(topicName, KafkaMetrics.ResultSuccess);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                KafkaSendActivity.Fail(activity, exception);
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds;
                kafkaMetrics.RecordProduceDuration(elapsed, topicName, KafkaMetrics.ResultError);
                kafkaMetrics.RecordProduceMessage(topicName, KafkaMetrics.ResultError);
                throw;
            }
        }
    }

    /// <summary>
    /// Returns the name of a consumer group declaration, a new <see cref="Guid"/> unless set, and rejects a name
    /// another declaration already has: a name resolves exactly one declaration's flow control.
    /// </summary>
    private string ReserveConsumerGroupName(string topicName, string groupId, string? name)
    {
        if (name is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(KafkaConsumerGroupBuilder<object, object>.Name));
        }

        name ??= Guid.NewGuid().ToString();
        if (consumerGroupNames.TryGetValue(name, out var existing))
        {
            throw new InvalidOperationException(
                $"Consumer group '{groupId}' on topic '{topicName}' and consumer group '{existing.GroupId}' on topic '{existing.Topic}' are both named '{name}'. Each consumer group declaration needs a unique name.");
        }

        consumerGroupNames.Add(name, (topicName, groupId));
        return name;
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

        var name = ReserveConsumerGroupName(topicName, groupId, groupBuilder.Name);
        services.AddKeyedSingleton(name, (sp, _) => new KafkaConsumerFlowControl(
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<KafkaConsumerFlowControl>()));
        services.AddKeyedSingleton<IConsumerFlowControl>(name, (sp, key) =>
            sp.GetRequiredKeyedService<KafkaConsumerFlowControl>(key).Application);

        services.AddSingleton<IHostedService>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // The declaration's flow control, shared by the application, the circuit breaker and the worker
            var flowControl = sp.GetRequiredKeyedService<KafkaConsumerFlowControl>(name);

            // Create circuit breaker observer if configured (shared across all workers)
            CircuitBreakerObserver<TValue>? cbObserver = null;
            if (config.CircuitBreakerConfig is not null)
            {
                cbObserver = new CircuitBreakerObserver<TValue>(
                    config.CircuitBreakerConfig,
                    name,
                    groupId,
                    topicName,
                    flowControl.CircuitBreaker,
                    sp.GetRequiredService<EmitMetrics>(),
                    loggerFactory.CreateLogger<CircuitBreakerObserver<TValue>>());
            }

            // Resolve observers once for error handling middleware (shared across all consumers)
            var consumeObservers = sp.GetServices<IConsumeObserver>().ToList();

            // The group's rate limiter is shared across all workers
            var rateLimitMiddleware = config.RateLimiter is { } rateLimiter
                ? new RateLimitMiddleware(rateLimiter, sp.GetRequiredService<EmitMetrics>())
                : null;

            // The action applied to a failure no retry follows: RetryMiddleware handles the retries
            Func<Exception, ErrorAction?>? errorPolicy = config.GroupErrorPolicy is { } policy
                ? ex => StripRetryAction(policy.Evaluate(ex))
                : null;

            var registration = new ConsumerGroupRegistration<TKey, TValue>
            {
                Name = name,
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
                    var composer = new ConsumerPipelineComposer<TValue>
                    {
                        Services = sp,
                        LoggerFactory = loggerFactory,
                        ValidationMiddleware = BuildValidationMiddleware(config.ValidationMiddleware),
                        FilterMiddleware = BuildFilterMiddleware(config.FilterMiddleware),
                        RetryPolicy = config.RetryPolicy,
                        ErrorPolicy = errorPolicy,
                        ConsumeObservers = consumeObservers,
                        GroupPipeline = config.GroupPipeline,
                        GlobalPipeline = globalPipeline,
                        GlobalInboundPipeline = globalInboundPipeline,
                        RateLimitMiddleware = rateLimitMiddleware,
                        ProviderInboundPipeline = config.KafkaInboundPipeline,
                        CircuitBreakerNotifier = cbObserver,
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
                            // The router only checks for a discard, so it gets the action without its retries
                            var unmatchedAction = errorPolicy?.Invoke(new UnmatchedRouteException(null));

                            var routerInvoker = routerReg.BuildInvoker(
                                type => new HandlerInvoker<TValue>(type),
                                sp,
                                loggerFactory,
                                unmatchedAction);

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
                RateLimitEnabled = config.RateLimiter is not null,
                BatchOptions = config.BatchOptions,
                BuildBatchConsumerPipelines = config.BatchInvokerEntry.HasValue
                    ? () =>
                    {
                        var (adapterType, invoker, userType) = config.BatchInvokerEntry.Value;

                        // Build the per-item middleware (single-mode shape) and lift each into
                        // a batch-mode wrapper via BatchPerItemAdapter. No batch-specific
                        // middleware classes — same code as single, run per item.
                        var innerValidation = BuildValidationMiddleware(config.ValidationMiddleware);
                        var batchValidation = innerValidation is not null
                            ? new BatchPerItemAdapter<TValue>(_ => innerValidation)
                            : null;

                        var innerFilter = BuildFilterMiddleware(config.FilterMiddleware);
                        var batchFilter = innerFilter is not null
                            ? new BatchPerItemAdapter<TValue>(_ => innerFilter)
                            : null;

                        var batchComposer = new ConsumerPipelineComposer<MessageBatch<TValue>>
                        {
                            Services = sp,
                            LoggerFactory = loggerFactory,
                            ValidationMiddleware = batchValidation,
                            FilterMiddleware = batchFilter,
                            RetryPolicy = config.RetryPolicy,
                            ErrorPolicy = errorPolicy,
                            ConsumeObservers = consumeObservers,
                            GroupPipeline = config.GroupPipeline,
                            GlobalPipeline = globalPipeline,
                            GlobalInboundPipeline = globalInboundPipeline,
                            RateLimitMiddleware = rateLimitMiddleware,
                            ProviderInboundPipeline = config.KafkaInboundPipeline,
                            CircuitBreakerNotifier = cbObserver,
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
            MarkIfTransactional(consumerType);
        }

        if (groupBuilder.IsBatchMode && groupBuilder.BatchConsumerType is not null)
        {
            var batchConsumerType = groupBuilder.BatchConsumerType;
            services.TryAddScoped(batchConsumerType);
            MarkIfTransactional(batchConsumerType);

            var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), batchConsumerType);
            services.TryAddScoped(adapterType);
        }

        if (groupBuilder.Routers is { } routers)
        {
            foreach (var routedConsumerType in routers.SelectMany(r => r.ConsumerTypes))
            {
                MarkIfTransactional(routedConsumerType);
            }
        }
    }

    /// <summary>
    /// Records a decorated handler so the configuration can be validated centrally once every
    /// integration has registered, rather than failing at request time.
    /// </summary>
    private void MarkIfTransactional(Type handlerType)
    {
        if (Attribute.IsDefined(handlerType, typeof(TransactionalAttribute)))
        {
            services.AddSingleton(new TransactionalHandlerMarker(handlerType));
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
        var filterModule = groupBuilder.Filters;
        filterModule?.RegisterServices(services);

        var retryPolicy = BuildRetryPolicy(groupErrorPolicy);

        System.Threading.RateLimiting.RateLimiter? rateLimiter = null;
        if (groupBuilder.RateLimitAction is not null)
        {
            var rateLimitBuilder = new RateLimiting.RateLimitBuilder();
            groupBuilder.RateLimitAction(rateLimitBuilder);
            var totalCapacity = groupBuilder.IsBatchMode
                ? workerCount * groupBuilder.BatchOptions!.MaxSize
                : workerCount * bufferSize;
            rateLimiter = rateLimitBuilder.Build(totalCapacity);
        }

        CircuitBreakerConfig? circuitBreakerConfig = null;
        if (groupBuilder.CircuitBreakerAction is not null)
        {
            var cbBuilder = new CircuitBreakerBuilder();
            groupBuilder.CircuitBreakerAction(cbBuilder);
            circuitBreakerConfig = cbBuilder.Build();
        }

        var kafkaInbound = InboundLevel;
        var groupPipeline = groupBuilder.Pipeline;
        var batchOptions = groupBuilder.BatchOptions;
        var isBatchMode = groupBuilder.IsBatchMode;

        // The group's middleware is registered for single messages and composed into the pipeline of its batch
        // consumer, so in batch mode each must run on batches too
        if (isBatchMode)
        {
            groupPipeline.EnsureRunsOn<ConsumeContext<MessageBatch<TValue>>>();
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
            ValidationMiddleware = validationModule,
            FilterMiddleware = filterModule,
            RetryPolicy = retryPolicy,
            RateLimiter = rateLimiter,
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
        public required List<(Type ConsumerType, IMiddlewarePipeline<ConsumeContext<TValue>> Invoker, MessagePipelineBuilder? Pipeline)> InvokerEntries { get; init; }
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
        public required ValidationMiddleware<TValue>? ValidationMiddleware { get; init; }
        public required FilterMiddleware<TValue>? FilterMiddleware { get; init; }
        public required Func<Exception, ErrorAction?>? RetryPolicy { get; init; }
        public required System.Threading.RateLimiting.RateLimiter? RateLimiter { get; init; }
        public required CircuitBreakerConfig? CircuitBreakerConfig { get; init; }
        public required MessagePipelineBuilder KafkaInboundPipeline { get; init; }
        public required MessagePipelineBuilder GroupPipeline { get; init; }
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

    /// <summary>
    /// Returns the consumer group's <see cref="ValidationMiddleware{TValue}"/> as inbound middleware,
    /// or <see langword="null"/> if no validator is configured. The same single-mode middleware
    /// is returned for both single and batch consumers; batch consumers wrap it in
    /// <see cref="BatchPerItemAdapter{TItem}"/> at composition time.
    /// </summary>
    private static IMiddleware<ConsumeContext<TValue>>? BuildValidationMiddleware<TValue>(
        ValidationMiddleware<TValue>? module)
        => module is { IsConfigured: true } ? module : null;

    /// <summary>
    /// Returns the consumer group's <see cref="FilterMiddleware{TValue}"/> as inbound middleware,
    /// or <see langword="null"/> if no filters are registered. The same single-mode middleware
    /// is returned for both single and batch consumers; batch consumers wrap it in
    /// <see cref="BatchPerItemAdapter{TItem}"/> at composition time.
    /// </summary>
    private static IMiddleware<ConsumeContext<TValue>>? BuildFilterMiddleware<TValue>(
        FilterMiddleware<TValue>? module)
        => module is { HasEntries: true } ? module : null;

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
    /// Returns the policy evaluator <c>RetryMiddleware</c> decides retries with, or <c>null</c>
    /// when neither the default action nor any clause retries, so no retry layer is composed.
    /// </summary>
    private static Func<Exception, ErrorAction?>? BuildRetryPolicy(ErrorPolicy? policy)
    {
        if (policy is null)
        {
            return null;
        }

        var retries = policy.DefaultAction is ErrorAction.RetryAction
            || policy.Clauses.Any(c => c.Action is ErrorAction.RetryAction);

        return retries ? policy.Evaluate : null;
    }

    /// <summary>
    /// Strips <see cref="ErrorAction.RetryAction"/> wrapping from an error action,
    /// returning the exhaustion action. Retry is handled by <c>RetryMiddleware</c>;
    /// the error policy should only return dead-letter, discard, or no action.
    /// </summary>
    private static ErrorAction? StripRetryAction(ErrorAction? action) =>
        action is ErrorAction.RetryAction retry ? retry.ExhaustionAction : action;

    private sealed class SchemaRegistryMarker;
}
