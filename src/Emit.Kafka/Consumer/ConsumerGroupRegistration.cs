namespace Emit.Kafka.Consumer;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.Pipeline;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Immutable descriptor capturing all build-time state for a consumer group.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
internal sealed class ConsumerGroupRegistration<TKey, TValue>
{
    // ── Topic identity ──

    /// <summary>The Kafka topic name to subscribe to.</summary>
    public required string TopicName { get; init; }

    /// <summary>The consumer group ID.</summary>
    public required string GroupId { get; init; }

    /// <summary>Pre-built destination address URI for the topic.</summary>
    public required Uri DestinationAddress { get; init; }

    // ── Deserializers ──

    /// <summary>Synchronous key deserializer.</summary>
    public ConfluentKafka.IDeserializer<TKey>? KeyDeserializer { get; init; }

    /// <summary>Synchronous value deserializer.</summary>
    public ConfluentKafka.IDeserializer<TValue>? ValueDeserializer { get; init; }

    /// <summary>Async key deserializer.</summary>
    public ConfluentKafka.IAsyncDeserializer<TKey>? KeyAsyncDeserializer { get; init; }

    /// <summary>Async value deserializer.</summary>
    public ConfluentKafka.IAsyncDeserializer<TValue>? ValueAsyncDeserializer { get; init; }

    // ── Consumer pipelines ──

    /// <summary>
    /// Factory that builds typed middleware pipeline entries for fan-out, one per registered consumer or router.
    /// Called once per worker to enable per-worker middleware isolation.
    /// Each entry pairs a consumer name with its pipeline delegate.
    /// </summary>
    public required Func<IReadOnlyList<ConsumerPipelineEntry<TValue>>> BuildConsumerPipelines { get; init; }

    // ── Worker config ──

    /// <summary>Number of worker tasks.</summary>
    public required int WorkerCount { get; init; }

    /// <summary>Distribution strategy for routing messages.</summary>
    public required WorkerDistribution WorkerDistribution { get; init; }

    /// <summary>Bounded channel capacity per worker.</summary>
    public required int BufferSize { get; init; }

    /// <summary>Offset commit timer interval.</summary>
    public required TimeSpan CommitInterval { get; init; }

    /// <summary>Maximum drain timeout on stop.</summary>
    public required TimeSpan WorkerStopTimeout { get; init; }

    // ── Config actions ──

    /// <summary>Applies ConsumerConfig-specific overrides.</summary>
    public required Action<ConfluentKafka.ConsumerConfig> ApplyConsumerConfigOverrides { get; init; }

    /// <summary>Applies shared ClientConfig settings.</summary>
    public required Action<ConfluentKafka.ClientConfig> ApplyClientConfig { get; init; }

    // ── Error handling ──

    /// <summary>
    /// Group-level error policy that applies to all consumers in this group.
    /// <c>null</c> when no error handling is configured.
    /// </summary>
    public ErrorPolicy? GroupErrorPolicy { get; init; }

    /// <summary>
    /// The deserialization error action configured for this group, or <c>null</c> if not configured.
    /// Deserialization errors are handled before message fan-out.
    /// </summary>
    public ErrorAction? DeserializationErrorAction { get; init; }

    /// <summary>
    /// Resolves the dead letter topic name from a source topic name using the global convention,
    /// or <c>null</c> if no dead letter convention is configured.
    /// </summary>
    public Func<string, string?>? ResolveDeadLetterTopic { get; init; }

    /// <summary>
    /// Immutable map of all dead letter topics resolved at registration time.
    /// </summary>
    public required DeadLetterTopicMap DeadLetterTopicMap { get; init; }

    /// <summary>
    /// All registered consumer handler types for this group.
    /// </summary>
    public IReadOnlyList<Type> ConsumerTypes { get; init; } = [];

    /// <summary>
    /// Whether circuit breaker is enabled for this consumer group.
    /// </summary>
    public bool CircuitBreakerEnabled { get; init; }

    /// <summary>
    /// Whether rate limiting is enabled for this consumer group.
    /// </summary>
    public bool RateLimitEnabled { get; init; }

    /// <summary>
    /// Builds a fully configured <see cref="ConfluentKafka.ConsumerConfig"/>.
    /// </summary>
    internal ConfluentKafka.ConsumerConfig BuildConsumerConfig()
    {
        var config = new ConfluentKafka.ConsumerConfig();
        ApplyClientConfig(config);
        ApplyConsumerConfigOverrides(config);
        config.GroupId = GroupId;
        config.EnableAutoCommit = false;
        config.EnableAutoOffsetStore = false;
        return config;
    }
}
