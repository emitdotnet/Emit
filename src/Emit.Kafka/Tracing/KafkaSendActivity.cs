namespace Emit.Kafka.Tracing;

using System.Diagnostics;
using Emit.Tracing;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// The <c>send</c> span of a message sent to Kafka, directly, from the outbox, or to a dead letter topic. It starts
/// before the send and ends with where the broker wrote the message, or with why the send failed.
/// </summary>
internal static class KafkaSendActivity
{
    /// <summary>
    /// Starts the span of sending a message.
    /// </summary>
    /// <param name="source">The source of the span: the producer's, or the consumer's for a dead letter.</param>
    /// <param name="options">The tracing options.</param>
    /// <param name="kind">Producer when the send creates the message, Client when a create span did.</param>
    /// <param name="destination">The address of the topic.</param>
    /// <param name="messageId">The message id, when it has one.</param>
    /// <param name="isTombstone">Whether the message has a key and no value.</param>
    /// <param name="parent">The parent context, or the default to use the current activity.</param>
    /// <param name="tags">More attributes to give at creation.</param>
    /// <returns>The started span, or <see langword="null"/> when none is started.</returns>
    public static Activity? Start(
        ActivitySource source,
        EmitTracingOptions options,
        ActivityKind kind,
        Uri destination,
        string? messageId,
        bool isTombstone,
        ActivityContext parent = default,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        List<KeyValuePair<string, object?>> sendTags = [.. tags ?? []];
        if (isTombstone)
        {
            sendTags.Add(new(KafkaActivityTagNames.MessageTombstone, true));
        }

        return MessagingActivity.Start(source, options, ActivityNames.Send, kind, destination, messageId, parent, tags: sendTags);
    }

    /// <summary>
    /// Records where the broker wrote the message.
    /// </summary>
    /// <param name="activity">The span of the send, or <see langword="null"/> when none was started.</param>
    /// <param name="partition">The partition the message was written to.</param>
    /// <param name="offset">The offset of the message in the partition.</param>
    public static void Complete(Activity? activity, ConfluentKafka.Partition partition, ConfluentKafka.Offset offset)
    {
        activity?.SetTag(KafkaActivityTagNames.DestinationPartitionId, partition.Value.ToString());
        activity?.SetTag(KafkaActivityTagNames.Offset, offset.Value);
    }

    /// <summary>
    /// Records that the broker did not write the message, with the Kafka error code as <c>error.type</c>.
    /// </summary>
    /// <param name="activity">The span of the send, or <see langword="null"/> when none was started.</param>
    /// <param name="error">The error the send failed with.</param>
    public static void Fail(Activity? activity, ConfluentKafka.Error error) =>
        MessagingActivity.RecordFailure(activity, error.Code.ToString(), error.Reason);

    /// <summary>
    /// Records that the send failed with <paramref name="exception"/>: with its Kafka error code when it is a Kafka
    /// error, so every failed send reports the same <c>error.type</c>, or with its type otherwise.
    /// </summary>
    /// <param name="activity">The span of the send, or <see langword="null"/> when none was started.</param>
    /// <param name="exception">The exception the send failed with.</param>
    public static void Fail(Activity? activity, Exception exception)
    {
        if (exception is ConfluentKafka.KafkaException kafkaException)
        {
            Fail(activity, kafkaException.Error);
        }
        else
        {
            MessagingActivity.RecordFailure(activity, exception);
        }
    }
}
