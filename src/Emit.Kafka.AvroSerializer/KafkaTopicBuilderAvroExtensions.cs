namespace Emit.Kafka.DependencyInjection;

using ConfluentSchemaRegistry = Confluent.SchemaRegistry;
using ConfluentSerdes = Confluent.SchemaRegistry.Serdes;

/// <summary>
/// Extension methods for <see cref="KafkaTopicBuilder{TKey, TValue}"/> providing
/// Avro serializer and deserializer declarations backed by a schema registry.
/// </summary>
public static class KafkaTopicBuilderAvroExtensions
{
    // ── Key serializer ──

    /// <summary>
    /// Sets an Avro key serializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetAvroKeySerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSerdes.AvroSerializerConfig? config = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
    {
        builder.SetKeyAsyncSerializerFactory(client => new ConfluentSerdes.AvroSerializer<TKey>(client, config, ruleRegistry));
    }

    // ── Value serializer ──

    /// <summary>
    /// Sets an Avro value serializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetAvroValueSerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSerdes.AvroSerializerConfig? config = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
    {
        builder.SetValueAsyncSerializerFactory(client => new ConfluentSerdes.AvroSerializer<TValue>(client, config, ruleRegistry));
    }

    // ── Key deserializer ──

    /// <summary>
    /// Sets an Avro key deserializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    public static void SetAvroKeyDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        IEnumerable<KeyValuePair<string, string>>? config = null)
    {
        builder.SetKeyAsyncDeserializerFactory(client => new ConfluentSerdes.AvroDeserializer<TKey>(client, config));
    }

    // ── Value deserializer ──

    /// <summary>
    /// Sets an Avro value deserializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    public static void SetAvroValueDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        IEnumerable<KeyValuePair<string, string>>? config = null)
    {
        builder.SetValueAsyncDeserializerFactory(client => new ConfluentSerdes.AvroDeserializer<TValue>(client, config));
    }
}
