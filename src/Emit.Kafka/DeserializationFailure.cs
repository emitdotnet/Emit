namespace Emit.Kafka;

using System.Runtime.Serialization;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

/// <summary>
/// Tells a deserializer failure caused by the bytes of the message, which no retry fixes, from any other failure,
/// such as a schema registry that cannot be reached, which is retried until the message is deserialized.
/// Only failures known to mean invalid data count as such: anything else is retried.
/// </summary>
internal static class DeserializationFailure
{
    /// <summary>
    /// Returns how failures of an async deserializer are classified: <see cref="InvalidDataException"/>, or
    /// what <paramref name="isInvalidData"/> recognizes, means invalid data.
    /// </summary>
    internal static Func<Exception, bool> ForAsyncDeserializer(Func<Exception, bool>? isInvalidData) =>
        Combine(static ex => ex is InvalidDataException, isInvalidData);

    /// <summary>
    /// Returns how failures of a schema registry deserializer are classified. On top of what
    /// <paramref name="isInvalidData"/> recognizes for its format, invalid data is a payload whose framing is wrong
    /// (an invalid magic byte, or too few bytes for the schema id) and a message failing a data quality rule.
    /// </summary>
    internal static Func<Exception, bool> ForSchemaRegistryDeserializer(Func<Exception, bool>? isInvalidData) =>
        Combine(
            static ex => ex is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException
                || ex is SerializationException { InnerException: ConfluentSchemaRegistry.RuleConditionException },
            isInvalidData);

    private static Func<Exception, bool> Combine(Func<Exception, bool> rule, Func<Exception, bool>? isInvalidData) =>
        isInvalidData is null ? rule : ex => rule(ex) || isInvalidData(ex);
}
