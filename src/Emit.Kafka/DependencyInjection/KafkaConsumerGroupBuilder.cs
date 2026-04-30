namespace Emit.Kafka.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.Kafka.Consumer;
using Emit.Pipeline;
using Emit.Pipeline.Modules;
using Emit.RateLimiting;
using Emit.Routing;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Configures a consumer group for a topic: consumer config overrides,
/// worker pool settings, and consumer handler registrations.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class KafkaConsumerGroupBuilder<TKey, TValue> : IConsumerGroupConfigurable<TValue>
{
    private readonly List<Type> consumerTypes = [];
    private readonly HashSet<Type> registeredConsumerTypes = [];
    private readonly Dictionary<Type, IMessagePipelineBuilder> consumerPipelines = new();
    private readonly KafkaConsumerOptions consumerConfig = new();

    // ── Consumer config (delegates to KafkaConsumerOptions) ──

    /// <inheritdoc cref="KafkaConsumerOptions.AutoOffsetReset"/>
    public ConfluentKafka.AutoOffsetReset? AutoOffsetReset
    {
        get => consumerConfig.AutoOffsetReset;
        set => consumerConfig.AutoOffsetReset = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.SessionTimeout"/>
    public TimeSpan? SessionTimeout
    {
        get => consumerConfig.SessionTimeout;
        set => consumerConfig.SessionTimeout = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.HeartbeatInterval"/>
    public TimeSpan? HeartbeatInterval
    {
        get => consumerConfig.HeartbeatInterval;
        set => consumerConfig.HeartbeatInterval = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.MaxPollInterval"/>
    public TimeSpan? MaxPollInterval
    {
        get => consumerConfig.MaxPollInterval;
        set => consumerConfig.MaxPollInterval = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.FetchMinBytes"/>
    public int? FetchMinBytes
    {
        get => consumerConfig.FetchMinBytes;
        set => consumerConfig.FetchMinBytes = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.FetchMaxBytes"/>
    public int? FetchMaxBytes
    {
        get => consumerConfig.FetchMaxBytes;
        set => consumerConfig.FetchMaxBytes = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.FetchWaitMax"/>
    public TimeSpan? FetchWaitMax
    {
        get => consumerConfig.FetchWaitMax;
        set => consumerConfig.FetchWaitMax = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.MaxPartitionFetchBytes"/>
    public int? MaxPartitionFetchBytes
    {
        get => consumerConfig.MaxPartitionFetchBytes;
        set => consumerConfig.MaxPartitionFetchBytes = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.GroupInstanceId"/>
    public string? GroupInstanceId
    {
        get => consumerConfig.GroupInstanceId;
        set => consumerConfig.GroupInstanceId = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.PartitionAssignmentStrategy"/>
    public ConfluentKafka.PartitionAssignmentStrategy? PartitionAssignmentStrategy
    {
        get => consumerConfig.PartitionAssignmentStrategy;
        set => consumerConfig.PartitionAssignmentStrategy = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.IsolationLevel"/>
    public ConfluentKafka.IsolationLevel? IsolationLevel
    {
        get => consumerConfig.IsolationLevel;
        set => consumerConfig.IsolationLevel = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.CheckCrcs"/>
    public bool? CheckCrcs
    {
        get => consumerConfig.CheckCrcs;
        set => consumerConfig.CheckCrcs = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.GroupProtocol"/>
    public ConfluentKafka.GroupProtocol? GroupProtocol
    {
        get => consumerConfig.GroupProtocol;
        set => consumerConfig.GroupProtocol = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.QueuedMaxMessagesKbytes"/>
    public int? QueuedMaxMessagesKbytes
    {
        get => consumerConfig.QueuedMaxMessagesKbytes;
        set => consumerConfig.QueuedMaxMessagesKbytes = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.QueuedMinMessages"/>
    public int? QueuedMinMessages
    {
        get => consumerConfig.QueuedMinMessages;
        set => consumerConfig.QueuedMinMessages = value;
    }

    /// <inheritdoc cref="KafkaConsumerOptions.AdditionalProperties"/>
    public Dictionary<string, string>? AdditionalProperties
    {
        get => consumerConfig.AdditionalProperties;
        set => consumerConfig.AdditionalProperties = value;
    }

    /// <inheritdoc />
    IMessagePipelineBuilder IInboundConfigurable.InboundPipeline => Pipeline;

    /// <summary>
    /// Gets the per-consumer-group middleware pipeline builder. Middleware registered here
    /// wraps only the consumer handlers in this consumer group.
    /// </summary>
    internal IMessagePipelineBuilder Pipeline { get; } = new MessagePipelineBuilder();

    // ── Worker pool configuration ──

    /// <summary>Number of processing tasks in the worker pool.</summary>
    public int WorkerCount { get; set; } = 1;

    /// <summary>How messages are routed from poll loop to workers.</summary>
    public WorkerDistribution WorkerDistribution { get; set; } = WorkerDistribution.ByKeyHash;

    /// <summary>Bounded channel capacity per worker.</summary>
    public int BufferSize { get; set; } = 32;

    /// <summary>Offset commit interval.</summary>
    public TimeSpan CommitInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum time to wait for workers to drain on stop.</summary>
    public TimeSpan WorkerStopTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // ── Internal state ──

    /// <summary>Consumer types registered via <see cref="AddConsumer{TConsumer}()"/>.</summary>
    internal IReadOnlyList<Type> ConsumerTypes => consumerTypes;

    /// <summary>Per-consumer middleware pipelines keyed by consumer type.</summary>
    internal IReadOnlyDictionary<Type, IMessagePipelineBuilder> ConsumerPipelines => consumerPipelines;

    /// <summary>Group-level error policy configuration, or <c>null</c> if not configured.</summary>
    internal Action<ErrorPolicyBuilder>? GroupErrorPolicyAction { get; private set; }

    /// <summary>Group-level validation module, or <c>null</c> if not configured.</summary>
    internal ValidationModule<TValue>? Validation { get; private set; }

    /// <summary>Deserialization error action configuration, or <c>null</c> if not configured.</summary>
    internal Action<ErrorActionBuilder>? DeserializationErrorAction { get; private set; }

    /// <summary>Rate limit configuration, or <c>null</c> if not configured.</summary>
    internal Action<RateLimitBuilder>? RateLimitAction { get; private set; }

    /// <summary>Circuit breaker configuration, or <c>null</c> if not configured.</summary>
    internal Action<CircuitBreakerBuilder>? CircuitBreakerAction { get; private set; }

    /// <summary>Router registrations, or <c>null</c> if no routers are configured.</summary>
    internal List<RouterRegistration<TValue>>? Routers { get; private set; }

    private HashSet<string>? registeredRouterIdentifiers;
    private BatchOptions? batchConfig;
    private Type? batchConsumerType;

    /// <summary>
    /// Creates a new consumer group builder.
    /// </summary>
    internal KafkaConsumerGroupBuilder()
    {
    }

    // ── Public fluent methods (return concrete type for maximum chaining) ──

    /// <summary>
    /// Registers a middleware type on this consumer group's inbound pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type.</typeparam>
    /// <param name="lifetime">Controls when the middleware instance is created.</param>
    /// <returns>This builder for continued chaining.</returns>
    public KafkaConsumerGroupBuilder<TKey, TValue> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
        where TMiddleware : class, IMiddleware<ConsumeContext<TValue>>
    {
        Pipeline.Use(typeof(TMiddleware), lifetime);
        return this;
    }

    /// <summary>
    /// Registers a consumer filter on this consumer group's inbound pipeline.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>This builder for continued chaining.</returns>
    public KafkaConsumerGroupBuilder<TKey, TValue> Filter<TFilter>()
        where TFilter : class, IConsumerFilter<TValue>
    {
        Pipeline.AddConsumerFilter<TValue, TFilter>();
        return this;
    }

    /// <summary>
    /// Configures error handling for all consumers in this group. When an exception
    /// occurs during message processing, the configured policy determines whether to
    /// dead-letter or discard the message.
    /// </summary>
    /// <param name="configure">Configures the error policy.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">OnError has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> OnError(Action<ErrorPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (GroupErrorPolicyAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(OnError)} has already been called on this consumer group builder.");
        }

        GroupErrorPolicyAction = configure;
        return this;
    }

    /// <summary>
    /// Registers a class-based message validator that is resolved from the service provider
    /// for each message. Validation failures throw <see cref="MessageValidationException"/>;
    /// the <paramref name="configureAction"/> determines whether to dead-letter or discard them.
    /// </summary>
    /// <typeparam name="TValidator">The validator type.</typeparam>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">Validate has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> Validate<TValidator>(Action<ErrorActionBuilder> configureAction)
        where TValidator : class, IMessageValidator<TValue>
    {
        ArgumentNullException.ThrowIfNull(configureAction);
        EnsureValidateNotAlreadyCalled();
        var module = new ValidationModule<TValue>();
        module.Configure<TValidator>(configureAction);
        Validation = module;
        return this;
    }

    /// <summary>
    /// Registers an inline async delegate validator. Validation failures throw
    /// <see cref="MessageValidationException"/>; the <paramref name="configureAction"/>
    /// determines whether to dead-letter or discard them.
    /// </summary>
    /// <param name="validator">The async validation delegate.</param>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">Validate has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> Validate(
        Func<TValue, CancellationToken, Task<MessageValidationResult>> validator,
        Action<ErrorActionBuilder> configureAction)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(configureAction);
        EnsureValidateNotAlreadyCalled();
        var module = new ValidationModule<TValue>();
        module.Configure(validator, configureAction);
        Validation = module;
        return this;
    }

    /// <summary>
    /// Registers an inline synchronous delegate validator. Validation failures throw
    /// <see cref="MessageValidationException"/>; the <paramref name="configureAction"/>
    /// determines whether to dead-letter or discard them.
    /// </summary>
    /// <param name="validator">The synchronous validation delegate.</param>
    /// <param name="configureAction">Configures the terminal action for validation failures.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">Validate has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> Validate(
        Func<TValue, MessageValidationResult> validator,
        Action<ErrorActionBuilder> configureAction)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(configureAction);
        EnsureValidateNotAlreadyCalled();
        var module = new ValidationModule<TValue>();
        module.Configure(validator, configureAction);
        Validation = module;
        return this;
    }

    /// <summary>
    /// Configures how deserialization errors are handled before message fan-out.
    /// Deserialization errors occur when the raw Kafka message bytes cannot be converted
    /// to the expected key or value types.
    /// </summary>
    /// <param name="configure">Configures the deserialization error action.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">OnDeserializationError has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> OnDeserializationError(Action<ErrorActionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (DeserializationErrorAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(OnDeserializationError)} has already been called on this consumer group builder.");
        }

        DeserializationErrorAction = configure;
        return this;
    }

    /// <summary>
    /// Configures rate limiting for this consumer group. All workers share a single
    /// rate limiter, throttling the total message processing rate across the group.
    /// </summary>
    /// <param name="configure">Configures the rate limiting algorithm.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">RateLimit has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> RateLimit(Action<RateLimitBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (RateLimitAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(RateLimit)} has already been called on this consumer group builder.");
        }

        RateLimitAction = configure;
        return this;
    }

    /// <summary>
    /// Configures a circuit breaker for this consumer group. When the failure rate exceeds
    /// the configured threshold, all consumers in the group are paused. After the pause
    /// duration, a single probe message determines whether to close or re-open the circuit.
    /// </summary>
    /// <param name="configure">Configures the circuit breaker parameters.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="InvalidOperationException">CircuitBreaker has already been called.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> CircuitBreaker(Action<CircuitBreakerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (CircuitBreakerAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(CircuitBreaker)} has already been called on this consumer group builder.");
        }

        CircuitBreakerAction = configure;
        return this;
    }

    /// <summary>
    /// Registers a consumer handler type for this group (fan-out).
    /// </summary>
    /// <exception cref="InvalidOperationException">Duplicate consumer type.</exception>
    public void AddConsumer<TConsumer>() where TConsumer : class, IConsumer<TValue>
    {
        if (batchConfig is not null)
            throw new InvalidOperationException(
                $"{nameof(AddConsumer)}<T>() cannot be combined with {nameof(AddBatchConsumer)}<T>(). " +
                "Use a separate consumer group for single-message consumption.");

        var type = typeof(TConsumer);
        if (!registeredConsumerTypes.Add(type))
        {
            throw new InvalidOperationException(
                $"Consumer type '{type.Name}' has already been registered in this consumer group.");
        }

        consumerTypes.Add(type);
    }

    /// <summary>
    /// Registers a consumer handler type for this group with per-consumer middleware
    /// configuration. Middleware registered here wraps only this consumer's invocation,
    /// running after any group-level middleware.
    /// </summary>
    /// <param name="configure">Configures per-consumer middleware and filters.</param>
    /// <exception cref="InvalidOperationException">Duplicate consumer type.</exception>
    public void AddConsumer<TConsumer>(Action<KafkaConsumerHandlerBuilder<TValue>> configure)
        where TConsumer : class, IConsumer<TValue>
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (batchConfig is not null)
            throw new InvalidOperationException(
                $"{nameof(AddConsumer)}<T>() cannot be combined with {nameof(AddBatchConsumer)}<T>(). " +
                "Use a separate consumer group for single-message consumption.");

        var type = typeof(TConsumer);
        if (!registeredConsumerTypes.Add(type))
        {
            throw new InvalidOperationException(
                $"Consumer type '{type.Name}' has already been registered in this consumer group.");
        }

        var handlerBuilder = new KafkaConsumerHandlerBuilder<TValue>();
        configure(handlerBuilder);

        consumerTypes.Add(type);

        if (handlerBuilder.Pipeline.Descriptors.Count > 0)
        {
            consumerPipelines[type] = handlerBuilder.Pipeline;
        }
    }

    /// <summary>
    /// Registers a content-based message router that dispatches messages to one of several
    /// consumer handlers based on a route key extracted from the message.
    /// The router participates in fan-out alongside direct consumers. Unmatched messages
    /// throw <see cref="UnmatchedRouteException"/>, which can be handled via <c>OnError</c>.
    /// </summary>
    /// <typeparam name="TRouteKey">The route key type (e.g., <see langword="string"/> or an enum).</typeparam>
    /// <param name="identifier">
    /// A unique identifier for this router within the consumer group.
    /// Used in traces, metrics, and dead letter headers to distinguish routers.
    /// Must not exceed 128 characters.
    /// </param>
    /// <param name="selector">
    /// Extracts the route key from the consume context. Return <c>null</c> to indicate no match.
    /// </param>
    /// <param name="configure">Configures the routes for this router.</param>
    /// <returns>This builder for continued chaining.</returns>
    /// <exception cref="ArgumentException">Identifier is empty, whitespace, or exceeds 128 characters.</exception>
    /// <exception cref="InvalidOperationException">A router with the same identifier has already been registered.</exception>
    public KafkaConsumerGroupBuilder<TKey, TValue> AddRouter<TRouteKey>(
        string identifier,
        Func<ConsumeContext<TValue>, TRouteKey?> selector,
        Action<MessageRouterBuilder<TValue, TRouteKey>> configure)
        where TRouteKey : notnull
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);

        if (batchConfig is not null)
            throw new InvalidOperationException(
                $"{nameof(AddRouter)} cannot be combined with {nameof(AddBatchConsumer)}<T>(). " +
                "Content-based routing and batch consumers are mutually exclusive.");

        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Router identifier must not be empty or whitespace.", nameof(identifier));
        }

        if (identifier.Length > 128)
        {
            throw new ArgumentException(
                $"Router identifier must not exceed 128 characters (was {identifier.Length}).", nameof(identifier));
        }

        registeredRouterIdentifiers ??= [];
        if (!registeredRouterIdentifiers.Add(identifier))
        {
            throw new InvalidOperationException(
                $"A router with identifier '{identifier}' has already been registered in this consumer group.");
        }

        var routerBuilder = new MessageRouterBuilder<TValue, TRouteKey>();
        configure(routerBuilder);
        var registration = routerBuilder.Build(selector);
        registration.Identifier = identifier;
        (Routers ??= []).Add(registration);
        return this;
    }

    /// <summary>
    /// Registers a batch consumer for this group with optional batch configuration.
    /// Mutually exclusive with <see cref="AddConsumer{TConsumer}()"/> and <c>AddRouter</c>.
    /// Only one batch consumer per group is supported.
    /// </summary>
    /// <param name="configure">Optional batch configuration. When omitted, defaults are used (MaxSize = 100, Timeout = 5s).</param>
    public KafkaConsumerGroupBuilder<TKey, TValue> AddBatchConsumer<T>(Action<BatchOptions>? configure = null)
        where T : class, IBatchConsumer<TValue>
    {
        if (consumerTypes.Count > 0 || (Routers is { Count: > 0 }))
            throw new InvalidOperationException(
                $"{nameof(AddBatchConsumer)}<T>() cannot be combined with {nameof(AddConsumer)}() or {nameof(AddRouter)}(). " +
                "Use a separate consumer group for batch consumption.");

        if (batchConsumerType is not null)
            throw new InvalidOperationException(
                "Only one batch consumer per consumer group is supported.");

        batchConfig = new BatchOptions();
        configure?.Invoke(batchConfig);

        if (batchConfig.MaxSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchOptions.MaxSize),
                batchConfig.MaxSize,
                "Batch MaxSize must be greater than zero.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchConfig.Timeout, TimeSpan.Zero, nameof(BatchOptions.Timeout));

        batchConsumerType = typeof(T);
        return this;
    }

    /// <summary>
    /// Applies non-null ConsumerConfig overrides onto a <see cref="ConfluentKafka.ConsumerConfig"/>.
    /// </summary>
    internal void ApplyTo(ConfluentKafka.ConsumerConfig config)
    {
        consumerConfig.ApplyTo(config);
    }

    internal BatchOptions? BatchOptions => batchConfig;
    internal Type? BatchConsumerType => batchConsumerType;
    internal bool IsBatchMode => batchConfig is not null;

    // ── Explicit interface implementations ──

    IConsumerGroupConfigurable<TValue> IConsumerGroupConfigurable<TValue>.OnError(Action<ErrorPolicyBuilder> configure) => OnError(configure);

    IConsumerGroupConfigurable<TValue> IConsumerGroupConfigurable<TValue>.Validate<TValidator>(Action<ErrorActionBuilder> configureAction) => Validate<TValidator>(configureAction);

    IConsumerGroupConfigurable<TValue> IConsumerGroupConfigurable<TValue>.Validate(
        Func<TValue, CancellationToken, Task<MessageValidationResult>> validator,
        Action<ErrorActionBuilder> configureAction) => Validate(validator, configureAction);

    IConsumerGroupConfigurable<TValue> IConsumerGroupConfigurable<TValue>.Validate(
        Func<TValue, MessageValidationResult> validator,
        Action<ErrorActionBuilder> configureAction) => Validate(validator, configureAction);

    IInboundConfigurable<TValue> IInboundConfigurable<TValue>.Use<TMiddleware>(MiddlewareLifetime lifetime) => Use<TMiddleware>(lifetime);

    IInboundConfigurable<TValue> IInboundConfigurable<TValue>.Filter<TFilter>() => Filter<TFilter>();

    private void EnsureValidateNotAlreadyCalled()
    {
        if (Validation is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(Validate)} has already been called on this consumer group builder.");
        }
    }
}
