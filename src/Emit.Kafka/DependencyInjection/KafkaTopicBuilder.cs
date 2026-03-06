namespace Emit.Kafka.DependencyInjection;

using ConfluentKafka = Confluent.Kafka;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

/// <summary>
/// Configures a single Kafka topic: serializers/deserializers, an optional producer,
/// and zero or more consumer groups.
/// </summary>
/// <typeparam name="TKey">The message key type.</typeparam>
/// <typeparam name="TValue">The message value type.</typeparam>
public sealed class KafkaTopicBuilder<TKey, TValue>
{
    private readonly string topicName;
    private readonly HashSet<string> registeredGroupIds = new(StringComparer.Ordinal);
    private readonly List<(string GroupId, KafkaConsumerGroupBuilder<TKey, TValue> Builder)> consumerGroups = [];

    // ── Serializers ──

    /// <summary>Synchronous key serializer.</summary>
    public ConfluentKafka.ISerializer<TKey>? KeySerializer { get; private set; }

    /// <summary>Synchronous value serializer.</summary>
    public ConfluentKafka.ISerializer<TValue>? ValueSerializer { get; private set; }

    /// <summary>Async key serializer.</summary>
    public ConfluentKafka.IAsyncSerializer<TKey>? KeyAsyncSerializer { get; private set; }

    /// <summary>Async value serializer.</summary>
    public ConfluentKafka.IAsyncSerializer<TValue>? ValueAsyncSerializer { get; private set; }

    // ── Deserializers ──

    /// <summary>Synchronous key deserializer.</summary>
    public ConfluentKafka.IDeserializer<TKey>? KeyDeserializer { get; private set; }

    /// <summary>Synchronous value deserializer.</summary>
    public ConfluentKafka.IDeserializer<TValue>? ValueDeserializer { get; private set; }

    /// <summary>Async key deserializer.</summary>
    public ConfluentKafka.IAsyncDeserializer<TKey>? KeyAsyncDeserializer { get; private set; }

    /// <summary>Async value deserializer.</summary>
    public ConfluentKafka.IAsyncDeserializer<TValue>? ValueAsyncDeserializer { get; private set; }

    // ── Deferred serializer/deserializer factories (resolved at DI time) ──

    /// <summary>Factory for creating the async key serializer from a schema registry client.</summary>
    internal Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncSerializer<TKey>>? KeyAsyncSerializerFactory { get; private set; }

    /// <summary>Factory for creating the async value serializer from a schema registry client.</summary>
    internal Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncSerializer<TValue>>? ValueAsyncSerializerFactory { get; private set; }

    /// <summary>Factory for creating the async key deserializer from a schema registry client.</summary>
    internal Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncDeserializer<TKey>>? KeyAsyncDeserializerFactory { get; private set; }

    /// <summary>Factory for creating the async value deserializer from a schema registry client.</summary>
    internal Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncDeserializer<TValue>>? ValueAsyncDeserializerFactory { get; private set; }

    // ── Internal state ──

    /// <summary>Whether <see cref="Producer"/> was called.</summary>
    internal bool ProducerConfigured { get; private set; }

    /// <summary>The producer builder, if <see cref="Producer"/> was called.</summary>
    internal KafkaProducerBuilder<TKey, TValue>? ProducerBuilder { get; private set; }

    /// <summary>Consumer group builders collected by <see cref="ConsumerGroup"/> calls.</summary>
    internal IReadOnlyList<(string GroupId, KafkaConsumerGroupBuilder<TKey, TValue> Builder)> ConsumerGroups => consumerGroups;

    /// <summary>
    /// Creates a new topic builder.
    /// </summary>
    internal KafkaTopicBuilder(string topicName)
    {
        this.topicName = topicName;
    }

    /// <summary>
    /// Sets the synchronous key serializer. Mutually exclusive with the async overload.
    /// </summary>
    /// <param name="serializer">The key serializer.</param>
    public void SetKeySerializer(ConfluentKafka.ISerializer<TKey> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ThrowIfAlreadyConfigured(KeyAsyncSerializer, KeyAsyncSerializerFactory, "key serializer");
        KeySerializer = serializer;
    }

    /// <summary>
    /// Sets the async key serializer. Mutually exclusive with the sync overload and factory overload.
    /// </summary>
    /// <param name="serializer">The async key serializer.</param>
    public void SetKeySerializer(ConfluentKafka.IAsyncSerializer<TKey> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ThrowIfAlreadyConfigured(KeySerializer, KeyAsyncSerializerFactory, "key serializer");
        KeyAsyncSerializer = serializer;
    }

    /// <summary>
    /// Sets the synchronous value serializer. Mutually exclusive with the async overload and factory overload.
    /// </summary>
    /// <param name="serializer">The value serializer.</param>
    public void SetValueSerializer(ConfluentKafka.ISerializer<TValue> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ThrowIfAlreadyConfigured(ValueAsyncSerializer, ValueAsyncSerializerFactory, "value serializer");
        ValueSerializer = serializer;
    }

    /// <summary>
    /// Sets the async value serializer. Mutually exclusive with the sync overload and factory overload.
    /// </summary>
    /// <param name="serializer">The async value serializer.</param>
    public void SetValueSerializer(ConfluentKafka.IAsyncSerializer<TValue> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ThrowIfAlreadyConfigured(ValueSerializer, ValueAsyncSerializerFactory, "value serializer");
        ValueAsyncSerializer = serializer;
    }

    /// <summary>
    /// Sets the synchronous key deserializer. Mutually exclusive with the async overload and factory overload.
    /// </summary>
    /// <param name="deserializer">The key deserializer.</param>
    public void SetKeyDeserializer(ConfluentKafka.IDeserializer<TKey> deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        ThrowIfAlreadyConfigured(KeyAsyncDeserializer, KeyAsyncDeserializerFactory, "key deserializer");
        KeyDeserializer = deserializer;
    }

    /// <summary>
    /// Sets the async key deserializer. Mutually exclusive with the sync overload and factory overload.
    /// </summary>
    /// <param name="deserializer">The async key deserializer.</param>
    public void SetKeyDeserializer(ConfluentKafka.IAsyncDeserializer<TKey> deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        ThrowIfAlreadyConfigured(KeyDeserializer, KeyAsyncDeserializerFactory, "key deserializer");
        KeyAsyncDeserializer = deserializer;
    }

    /// <summary>
    /// Sets the synchronous value deserializer. Mutually exclusive with the async overload and factory overload.
    /// </summary>
    /// <param name="deserializer">The value deserializer.</param>
    public void SetValueDeserializer(ConfluentKafka.IDeserializer<TValue> deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        ThrowIfAlreadyConfigured(ValueAsyncDeserializer, ValueAsyncDeserializerFactory, "value deserializer");
        ValueDeserializer = deserializer;
    }

    /// <summary>
    /// Sets the async value deserializer. Mutually exclusive with the sync overload and factory overload.
    /// </summary>
    /// <param name="deserializer">The async value deserializer.</param>
    public void SetValueDeserializer(ConfluentKafka.IAsyncDeserializer<TValue> deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        ThrowIfAlreadyConfigured(ValueDeserializer, ValueAsyncDeserializerFactory, "value deserializer");
        ValueAsyncDeserializer = deserializer;
    }

    /// <summary>
    /// Sets a deferred key serializer factory that will be resolved at DI time using the schema registry client.
    /// Mutually exclusive with sync and async key serializer overloads.
    /// </summary>
    /// <param name="factory">A factory that receives the schema registry client and returns an async key serializer.</param>
    /// <exception cref="InvalidOperationException">A key serializer has already been configured.</exception>
    public void SetKeyAsyncSerializerFactory(Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncSerializer<TKey>> factory)
    {
        ThrowIfAlreadyConfigured(KeySerializer, KeyAsyncSerializer, KeyAsyncSerializerFactory, "key serializer");
        KeyAsyncSerializerFactory = factory;
    }

    /// <summary>
    /// Sets a deferred value serializer factory that will be resolved at DI time using the schema registry client.
    /// Mutually exclusive with sync and async value serializer overloads.
    /// </summary>
    /// <param name="factory">A factory that receives the schema registry client and returns an async value serializer.</param>
    /// <exception cref="InvalidOperationException">A value serializer has already been configured.</exception>
    public void SetValueAsyncSerializerFactory(Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncSerializer<TValue>> factory)
    {
        ThrowIfAlreadyConfigured(ValueSerializer, ValueAsyncSerializer, ValueAsyncSerializerFactory, "value serializer");
        ValueAsyncSerializerFactory = factory;
    }

    /// <summary>
    /// Sets a deferred key deserializer factory that will be resolved at DI time using the schema registry client.
    /// Mutually exclusive with sync and async key deserializer overloads.
    /// </summary>
    /// <param name="factory">A factory that receives the schema registry client and returns an async key deserializer.</param>
    /// <exception cref="InvalidOperationException">A key deserializer has already been configured.</exception>
    public void SetKeyAsyncDeserializerFactory(Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncDeserializer<TKey>> factory)
    {
        ThrowIfAlreadyConfigured(KeyDeserializer, KeyAsyncDeserializer, KeyAsyncDeserializerFactory, "key deserializer");
        KeyAsyncDeserializerFactory = factory;
    }

    /// <summary>
    /// Sets a deferred value deserializer factory that will be resolved at DI time using the schema registry client.
    /// Mutually exclusive with sync and async value deserializer overloads.
    /// </summary>
    /// <param name="factory">A factory that receives the schema registry client and returns an async value deserializer.</param>
    /// <exception cref="InvalidOperationException">A value deserializer has already been configured.</exception>
    public void SetValueAsyncDeserializerFactory(Func<ConfluentSchemaRegistry.ISchemaRegistryClient, ConfluentKafka.IAsyncDeserializer<TValue>> factory)
    {
        ThrowIfAlreadyConfigured(ValueDeserializer, ValueAsyncDeserializer, ValueAsyncDeserializerFactory, "value deserializer");
        ValueAsyncDeserializerFactory = factory;
    }

    private static void ThrowIfAlreadyConfigured(object? conflicting1, object? conflicting2, string component)
    {
        if (conflicting1 is not null || conflicting2 is not null)
        {
            throw new InvalidOperationException($"A conflicting {component} has already been configured.");
        }
    }

    private static void ThrowIfAlreadyConfigured(object? conflicting1, object? conflicting2, object? conflicting3, string component)
    {
        if (conflicting1 is not null || conflicting2 is not null || conflicting3 is not null)
        {
            throw new InvalidOperationException($"A conflicting {component} has already been configured.");
        }
    }

    /// <summary>
    /// Declares a producer for this topic with optional configuration for
    /// producer settings and per-producer outbound middleware.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public void Producer(Action<KafkaProducerBuilder<TKey, TValue>>? configure = null)
    {
        if (ProducerConfigured)
        {
            throw new InvalidOperationException(
                $"{nameof(Producer)} has already been declared for topic '{topicName}'.");
        }

        ProducerConfigured = true;

        if (configure is not null)
        {
            var builder = new KafkaProducerBuilder<TKey, TValue>();
            configure(builder);
            ProducerBuilder = builder;
        }
    }

    /// <summary>
    /// Declares a consumer group for this topic.
    /// </summary>
    /// <exception cref="InvalidOperationException">Duplicate group ID on this topic.</exception>
    public void ConsumerGroup(
        string groupId,
        Action<KafkaConsumerGroupBuilder<TKey, TValue>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(configure);

        if (!registeredGroupIds.Add(groupId))
        {
            throw new InvalidOperationException(
                $"A consumer group with ID '{groupId}' has already been declared for topic '{topicName}'.");
        }

        var builder = new KafkaConsumerGroupBuilder<TKey, TValue>();
        configure(builder);

        if (builder.ConsumerTypes.Count == 0 && (builder.Routers is null || builder.Routers.Count == 0))
        {
            throw new InvalidOperationException(
                $"Consumer group '{groupId}' on topic '{topicName}' must have at least one consumer registered via AddConsumer or AddRouter.");
        }

        consumerGroups.Add((groupId, builder));
    }
}
