namespace Emit.Kafka.DependencyInjection;

using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Extension methods for <see cref="KafkaTopicBuilder{TKey, TValue}"/> providing
/// convenience serializer and deserializer declarations for built-in Kafka types.
/// </summary>
public static class KafkaTopicBuilderExtensions
{
    // ── Built-in: Utf8 (string) ──

    /// <summary>
    /// Configures UTF-8 serializers and deserializers for both key and value in a single call.
    /// Shorthand for calling <c>SetUtf8KeySerializer</c>, <c>SetUtf8ValueSerializer</c>,
    /// <c>SetUtf8KeyDeserializer</c>, and <c>SetUtf8ValueDeserializer</c>.
    /// </summary>
    public static void UseUtf8Serialization(this KafkaTopicBuilder<string, string> builder)
    {
        builder.SetUtf8KeySerializer();
        builder.SetUtf8ValueSerializer();
        builder.SetUtf8KeyDeserializer();
        builder.SetUtf8ValueDeserializer();
    }

    /// <summary>Sets the built-in UTF-8 key serializer.</summary>
    public static void SetUtf8KeySerializer<TValue>(this KafkaTopicBuilder<string, TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.Utf8);

    /// <summary>Sets the built-in UTF-8 value serializer.</summary>
    public static void SetUtf8ValueSerializer<TKey>(this KafkaTopicBuilder<TKey, string> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.Utf8);

    /// <summary>Sets the built-in UTF-8 key deserializer.</summary>
    public static void SetUtf8KeyDeserializer<TValue>(this KafkaTopicBuilder<string, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Utf8);

    /// <summary>Sets the built-in UTF-8 value deserializer.</summary>
    public static void SetUtf8ValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, string> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Utf8);

    // ── Built-in: ByteArray (byte[]) ──

    /// <summary>Sets the built-in byte array key serializer.</summary>
    public static void SetByteArrayKeySerializer<TValue>(this KafkaTopicBuilder<byte[], TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.ByteArray);

    /// <summary>Sets the built-in byte array value serializer.</summary>
    public static void SetByteArrayValueSerializer<TKey>(this KafkaTopicBuilder<TKey, byte[]> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.ByteArray);

    /// <summary>Sets the built-in byte array key deserializer.</summary>
    public static void SetByteArrayKeyDeserializer<TValue>(this KafkaTopicBuilder<byte[], TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.ByteArray);

    /// <summary>Sets the built-in byte array value deserializer.</summary>
    public static void SetByteArrayValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, byte[]> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.ByteArray);

    // ── Built-in: Null ──

    /// <summary>Sets the built-in null key serializer.</summary>
    public static void SetNullKeySerializer<TValue>(this KafkaTopicBuilder<ConfluentKafka.Null, TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.Null);

    /// <summary>Sets the built-in null value serializer.</summary>
    public static void SetNullValueSerializer<TKey>(this KafkaTopicBuilder<TKey, ConfluentKafka.Null> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.Null);

    /// <summary>Sets the built-in null key deserializer.</summary>
    public static void SetNullKeyDeserializer<TValue>(this KafkaTopicBuilder<ConfluentKafka.Null, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Null);

    /// <summary>Sets the built-in null value deserializer.</summary>
    public static void SetNullValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, ConfluentKafka.Null> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Null);

    // ── Built-in: Int32 (int) ──

    /// <summary>Sets the built-in 32-bit integer key serializer.</summary>
    public static void SetInt32KeySerializer<TValue>(this KafkaTopicBuilder<int, TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.Int32);

    /// <summary>Sets the built-in 32-bit integer value serializer.</summary>
    public static void SetInt32ValueSerializer<TKey>(this KafkaTopicBuilder<TKey, int> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.Int32);

    /// <summary>Sets the built-in 32-bit integer key deserializer.</summary>
    public static void SetInt32KeyDeserializer<TValue>(this KafkaTopicBuilder<int, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Int32);

    /// <summary>Sets the built-in 32-bit integer value deserializer.</summary>
    public static void SetInt32ValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, int> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Int32);

    // ── Built-in: Int64 (long) ──

    /// <summary>Sets the built-in 64-bit integer key serializer.</summary>
    public static void SetInt64KeySerializer<TValue>(this KafkaTopicBuilder<long, TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.Int64);

    /// <summary>Sets the built-in 64-bit integer value serializer.</summary>
    public static void SetInt64ValueSerializer<TKey>(this KafkaTopicBuilder<TKey, long> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.Int64);

    /// <summary>Sets the built-in 64-bit integer key deserializer.</summary>
    public static void SetInt64KeyDeserializer<TValue>(this KafkaTopicBuilder<long, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Int64);

    /// <summary>Sets the built-in 64-bit integer value deserializer.</summary>
    public static void SetInt64ValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, long> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Int64);

    // ── Built-in: Single (float) ──

    /// <summary>Sets the built-in single-precision floating-point key serializer.</summary>
    public static void SetSingleKeySerializer<TValue>(this KafkaTopicBuilder<float, TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.Single);

    /// <summary>Sets the built-in single-precision floating-point value serializer.</summary>
    public static void SetSingleValueSerializer<TKey>(this KafkaTopicBuilder<TKey, float> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.Single);

    /// <summary>Sets the built-in single-precision floating-point key deserializer.</summary>
    public static void SetSingleKeyDeserializer<TValue>(this KafkaTopicBuilder<float, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Single);

    /// <summary>Sets the built-in single-precision floating-point value deserializer.</summary>
    public static void SetSingleValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, float> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Single);

    // ── Built-in: Double (double) ──

    /// <summary>Sets the built-in double-precision floating-point key serializer.</summary>
    public static void SetDoubleKeySerializer<TValue>(this KafkaTopicBuilder<double, TValue> builder)
        => builder.SetKeySerializer(ConfluentKafka.Serializers.Double);

    /// <summary>Sets the built-in double-precision floating-point value serializer.</summary>
    public static void SetDoubleValueSerializer<TKey>(this KafkaTopicBuilder<TKey, double> builder)
        => builder.SetValueSerializer(ConfluentKafka.Serializers.Double);

    /// <summary>Sets the built-in double-precision floating-point key deserializer.</summary>
    public static void SetDoubleKeyDeserializer<TValue>(this KafkaTopicBuilder<double, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Double);

    /// <summary>Sets the built-in double-precision floating-point value deserializer.</summary>
    public static void SetDoubleValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, double> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Double);

    // ── Built-in: Ignore (deserializers only) ──

    /// <summary>Sets the built-in ignore key deserializer that discards key bytes.</summary>
    public static void SetIgnoreKeyDeserializer<TValue>(this KafkaTopicBuilder<ConfluentKafka.Ignore, TValue> builder)
        => builder.SetKeyDeserializer(ConfluentKafka.Deserializers.Ignore);

    /// <summary>Sets the built-in ignore value deserializer that discards value bytes.</summary>
    public static void SetIgnoreValueDeserializer<TKey>(this KafkaTopicBuilder<TKey, ConfluentKafka.Ignore> builder)
        => builder.SetValueDeserializer(ConfluentKafka.Deserializers.Ignore);
}
