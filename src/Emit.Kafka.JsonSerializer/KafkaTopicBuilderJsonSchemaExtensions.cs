namespace Emit.Kafka.DependencyInjection;

using ConfluentSchemaRegistry = Confluent.SchemaRegistry;
using ConfluentSerdes = Confluent.SchemaRegistry.Serdes;
using NJsonSchemaGeneration = NJsonSchema.NewtonsoftJson.Generation;

/// <summary>
/// Extension methods for <see cref="KafkaTopicBuilder{TKey, TValue}"/> providing
/// JSON Schema serializer and deserializer declarations backed by a schema registry.
/// </summary>
public static class KafkaTopicBuilderJsonSchemaExtensions
{
    // ── Key serializer ──

    /// <summary>
    /// Sets a JSON Schema key serializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetJsonSchemaKeySerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSerdes.JsonSerializerConfig? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
        where TKey : class
    {
        builder.SetKeyAsyncSerializerFactory(client =>
            new ConfluentSerdes.JsonSerializer<TKey>(client, config, jsonSchemaGeneratorSettings, ruleRegistry));
    }

    /// <summary>
    /// Sets a JSON Schema key serializer backed by the schema registry with an explicit schema for validation.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="schema">Schema to use for validation, used when external schema references are present.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetJsonSchemaKeySerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSchemaRegistry.Schema schema,
        ConfluentSerdes.JsonSerializerConfig? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
        where TKey : class
    {
        builder.SetKeyAsyncSerializerFactory(client =>
            new ConfluentSerdes.JsonSerializer<TKey>(client, schema, config, jsonSchemaGeneratorSettings, ruleRegistry));
    }

    // ── Value serializer ──

    /// <summary>
    /// Sets a JSON Schema value serializer backed by the schema registry.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetJsonSchemaValueSerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSerdes.JsonSerializerConfig? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
        where TValue : class
    {
        builder.SetValueAsyncSerializerFactory(client =>
            new ConfluentSerdes.JsonSerializer<TValue>(client, config, jsonSchemaGeneratorSettings, ruleRegistry));
    }

    /// <summary>
    /// Sets a JSON Schema value serializer backed by the schema registry with an explicit schema for validation.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="schema">Schema to use for validation, used when external schema references are present.</param>
    /// <param name="config">Optional serializer configuration.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    /// <param name="ruleRegistry">Optional rule registry for data quality rules.</param>
    public static void SetJsonSchemaValueSerializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSchemaRegistry.Schema schema,
        ConfluentSerdes.JsonSerializerConfig? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null,
        ConfluentSchemaRegistry.RuleRegistry? ruleRegistry = null)
        where TValue : class
    {
        builder.SetValueAsyncSerializerFactory(client =>
            new ConfluentSerdes.JsonSerializer<TValue>(client, schema, config, jsonSchemaGeneratorSettings, ruleRegistry));
    }

    // ── Key deserializer ──

    /// <summary>
    /// Sets a JSON Schema key deserializer. Does not require a schema registry.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    public static void SetJsonSchemaKeyDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        IEnumerable<KeyValuePair<string, string>>? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null)
        where TKey : class
    {
        builder.SetKeyDeserializer(
            new ConfluentSerdes.JsonDeserializer<TKey>(config, jsonSchemaGeneratorSettings));
    }

    /// <summary>
    /// Sets a JSON Schema key deserializer with an explicit schema for validation.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="schema">Schema to use for validation, used when external schema references are present.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    public static void SetJsonSchemaKeyDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSchemaRegistry.Schema schema,
        IEnumerable<KeyValuePair<string, string>>? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null)
        where TKey : class
    {
        builder.SetKeyAsyncDeserializerFactory(client =>
            new ConfluentSerdes.JsonDeserializer<TKey>(client, schema, config, jsonSchemaGeneratorSettings));
    }

    // ── Value deserializer ──

    /// <summary>
    /// Sets a JSON Schema value deserializer. Does not require a schema registry.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    public static void SetJsonSchemaValueDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        IEnumerable<KeyValuePair<string, string>>? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null)
        where TValue : class
    {
        builder.SetValueDeserializer(
            new ConfluentSerdes.JsonDeserializer<TValue>(config, jsonSchemaGeneratorSettings));
    }

    /// <summary>
    /// Sets a JSON Schema value deserializer with an explicit schema for validation.
    /// Requires <see cref="KafkaBuilder.ConfigureSchemaRegistry"/> to have been called.
    /// </summary>
    /// <param name="builder">The topic builder.</param>
    /// <param name="schema">Schema to use for validation, used when external schema references are present.</param>
    /// <param name="config">Optional deserializer configuration properties.</param>
    /// <param name="jsonSchemaGeneratorSettings">Optional JSON schema generator settings.</param>
    public static void SetJsonSchemaValueDeserializer<TKey, TValue>(
        this KafkaTopicBuilder<TKey, TValue> builder,
        ConfluentSchemaRegistry.Schema schema,
        IEnumerable<KeyValuePair<string, string>>? config = null,
        NJsonSchemaGeneration.NewtonsoftJsonSchemaGeneratorSettings? jsonSchemaGeneratorSettings = null)
        where TValue : class
    {
        builder.SetValueAsyncDeserializerFactory(client =>
            new ConfluentSerdes.JsonDeserializer<TValue>(client, schema, config, jsonSchemaGeneratorSettings));
    }

}
