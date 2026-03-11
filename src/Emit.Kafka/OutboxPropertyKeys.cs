namespace Emit.Kafka;

/// <summary>
/// Well-known keys used in <see cref="Models.OutboxEntry.Properties"/> by the Kafka provider.
/// </summary>
internal static class OutboxPropertyKeys
{
    /// <summary>The Kafka topic name.</summary>
    internal const string Topic = "topic";

    /// <summary>The .NET full type name of the message key.</summary>
    internal const string KeyType = "keyType";

    /// <summary>The .NET full type name of the message value.</summary>
    internal const string ValueType = "valueType";

    /// <summary>The serialized message key encoded as base64.</summary>
    internal const string Key = "key";
}
