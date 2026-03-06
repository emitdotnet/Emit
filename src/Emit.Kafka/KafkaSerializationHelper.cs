namespace Emit.Kafka;

using System.Text;
using Confluent.Kafka;

/// <summary>
/// Shared serialization utilities for Kafka producers and consumers.
/// </summary>
internal static class KafkaSerializationHelper
{
    /// <summary>
    /// Serializes a value using the configured sync or async serializer.
    /// </summary>
    internal static async Task<byte[]?> SerializeAsync<T>(
        T data,
        string topic,
        Headers? headers,
        ISerializer<T>? syncSerializer,
        IAsyncSerializer<T>? asyncSerializer,
        MessageComponentType componentType)
    {
        if (data is null)
        {
            return null;
        }

        // Include headers in context for full behavioral equivalence with Confluent.Kafka
        var context = new SerializationContext(componentType, topic, headers);

        // Async serializers take precedence (required for schema registry)
        if (asyncSerializer is not null)
        {
            return await asyncSerializer.SerializeAsync(data, context).ConfigureAwait(false);
        }

        if (syncSerializer is not null)
        {
            return syncSerializer.Serialize(data, context);
        }

        var componentName = componentType == MessageComponentType.Key ? "key" : "value";
        throw new InvalidOperationException(
            $"Cannot serialize {componentName} of type {typeof(T).Name}: no serializer configured. " +
            $"Configure a {componentName} serializer via {char.ToUpperInvariant(componentName[0])}{componentName[1..]}Serializer or {char.ToUpperInvariant(componentName[0])}{componentName[1..]}AsyncSerializer.");
    }

    /// <summary>
    /// Deserializes raw bytes into a typed value using sync or async deserializer.
    /// </summary>
    internal static async ValueTask<T> DeserializeAsync<T>(
        ReadOnlyMemory<byte> data,
        bool isNull,
        string topic,
        Headers headers,
        IDeserializer<T>? syncDeserializer,
        IAsyncDeserializer<T>? asyncDeserializer,
        MessageComponentType componentType)
    {
        var context = new SerializationContext(componentType, topic, headers);

        if (asyncDeserializer is not null)
        {
            return await asyncDeserializer.DeserializeAsync(data, isNull, context).ConfigureAwait(false);
        }

        if (syncDeserializer is not null)
        {
            return syncDeserializer.Deserialize(data.Span, isNull, context);
        }

        var componentName = componentType == MessageComponentType.Key ? "key" : "value";
        throw new InvalidOperationException(
            $"Cannot deserialize {componentName} of type {typeof(T).Name}: no deserializer configured. " +
            $"Configure a {componentName} deserializer via {char.ToUpperInvariant(componentName[0])}{componentName[1..]}Deserializer or {char.ToUpperInvariant(componentName[0])}{componentName[1..]}AsyncDeserializer.");
    }

    /// <summary>
    /// Converts string key-value headers to Confluent.Kafka <see cref="Headers"/>.
    /// </summary>
    internal static Headers? ConvertHeaders(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (headers.Count == 0)
        {
            return null;
        }

        var kafkaHeaders = new Headers();
        foreach (var (key, value) in headers)
        {
            kafkaHeaders.Add(key, Encoding.UTF8.GetBytes(value));
        }

        return kafkaHeaders;
    }
}
