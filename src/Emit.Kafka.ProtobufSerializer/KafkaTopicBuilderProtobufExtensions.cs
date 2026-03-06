namespace Emit.Kafka.DependencyInjection;

using Google.Protobuf;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;
using ConfluentSerdes = Confluent.SchemaRegistry.Serdes;

/// <summary>
/// Extension methods for <see cref="KafkaTopicBuilder{TKey, TValue}"/> providing
/// Protobuf serializer and deserializer declarations backed by a schema registry.
/// </summary>
public static class KafkaTopicBuilderProtobufExtensions
{
    // ── Key serializer ──

    /// <summary>
    /// Sets a Protobuf key serializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetProtobufKeySerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSerdes.ProtobufSerializerConfig? config = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
        where TKey : class, IMessage<TKey>, new()
    {
        builder.SetKeyAsyncSerializerFactory(client =>
            new ConfluentSerdes.ProtobufSerializer<TKey>(client, config, ruleRegistry));
    }

    // ── Value serializer ──

    /// <summary>
    /// Sets a Protobuf value serializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetProtobufValueSerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSerdes.ProtobufSerializerConfig? config = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
        where TValue : class, IMessage<TValue>, new()
    {
        builder.SetValueAsyncSerializerFactory(client =>
            new ConfluentSerdes.ProtobufSerializer<TValue>(client, config, ruleRegistry));
    }

    // ── Key deserializer ──

    /// <summary>
    /// Sets a Protobuf key deserializer. Does not require a schema registry.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    public static void SetProtobufKeyDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        IEnumerable<KeyValuePair<string, string>>? config = null)
        where TKey : class, IMessage<TKey>, new()
    {
        builder.SetKeyDeserializer(
            new ConfluentSerdes.ProtobufDeserializer<TKey>(config));
    }

    // ── Value deserializer ──

    /// <summary>
    /// Sets a Protobuf value deserializer. Does not require a schema registry.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    public static void SetProtobufValueDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        IEnumerable<KeyValuePair<string, string>>? config = null)
        where TValue : class, IMessage<TValue>, new()
    {
        builder.SetValueDeserializer(
            new ConfluentSerdes.ProtobufDeserializer<TValue>(config));
    }

}
