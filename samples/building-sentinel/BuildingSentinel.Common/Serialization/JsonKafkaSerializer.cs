namespace BuildingSentinel.Common.Serialization;

using System.Text.Json;
using Confluent.Kafka;

public sealed class JsonKafkaSerializer<T> : ISerializer<T>, IDeserializer<T>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public byte[] Serialize(T data, SerializationContext context)
        => JsonSerializer.SerializeToUtf8Bytes(data, Options);

    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        => isNull ? default! : JsonSerializer.Deserialize<T>(data, Options)!;
}
