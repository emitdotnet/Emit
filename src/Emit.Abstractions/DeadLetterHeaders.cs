namespace Emit.Abstractions;

/// <summary>
/// Well-known header name constants and factory methods for dead-letter messages.
/// </summary>
public static class DeadLetterHeaders
{
    /// <summary>The fully qualified exception type name.</summary>
    public const string ExceptionType = "emit-exception-type";

    /// <summary>The exception message.</summary>
    public const string ExceptionMessage = "emit-exception-message";

    /// <summary>The number of processing attempts made before dead-lettering.</summary>
    public const string RetryCount = "emit-retry-count";

    /// <summary>The UTC timestamp when the message was dead-lettered, in ISO 8601 round-trip format.</summary>
    public const string Timestamp = "emit-timestamp";

    /// <summary>The consumer group that failed to process the message.</summary>
    public const string ConsumerGroup = "emit-consumer-group";

    /// <summary>The consumer handler identifier that failed to process the message.</summary>
    public const string Consumer = "emit-consumer";

    /// <summary>The fully qualified type name of the consumer that failed to process the message.</summary>
    public const string ConsumerType = "emit-consumer-type";

    /// <summary>The route key matched when dispatching the message to the consumer.</summary>
    public const string RouteKey = "emit-route-key";

    /// <summary>The W3C traceparent from the original message, preserved for distributed trace replay linking.</summary>
    public const string OriginalTraceParent = "emit-dlq-original-traceparent";

    /// <summary>The source topic the message was originally consumed from.</summary>
    public const string OriginalTopic = "emit-dlq-original-topic";

    /// <summary>Prefix of the headers saying where the message was consumed from.</summary>
    public const string SourcePrefix = "emit-source-";

    /// <summary>The topic the message was consumed from.</summary>
    public const string SourceTopic = $"{SourcePrefix}topic";

    /// <summary>
    /// Creates a base dead-letter header list by preserving the message headers, including those the consume
    /// pipeline added (such as where the message was consumed from), and appending standard diagnostic headers
    /// derived from the exception.
    /// </summary>
    /// <param name="originalHeaders">The headers of the message, or <c>null</c> if unavailable.</param>
    /// <param name="exceptionType">The type of the exception that caused the dead-letter.</param>
    /// <param name="exceptionMessage">The exception message.</param>
    /// <returns>A mutable list of headers ready for caller-specific headers to be appended.</returns>
    public static List<KeyValuePair<string, string>> CreateBase(
        IReadOnlyList<KeyValuePair<string, string>>? originalHeaders,
        Type exceptionType,
        string exceptionMessage)
    {
        var headers = new List<KeyValuePair<string, string>>();

        if (originalHeaders is not null)
        {
            headers.AddRange(originalHeaders);
        }

        var traceparent = originalHeaders.GetFirstValue(WellKnownHeaders.TraceParent);
        if (!string.IsNullOrEmpty(traceparent))
        {
            headers.Upsert(OriginalTraceParent, traceparent);
        }

        // The last one: a replayed dead letter still carries the topic it was first consumed from
        var sourceTopic = originalHeaders.GetLastValue(SourceTopic);
        if (!string.IsNullOrEmpty(sourceTopic))
        {
            headers.Upsert(OriginalTopic, sourceTopic);
        }

        // A dead letter describes one failure: one that is dead-lettered again replaces what it carried
        headers.Upsert(ExceptionType, exceptionType.FullName ?? exceptionType.Name);
        headers.Upsert(ExceptionMessage, exceptionMessage);
        headers.Upsert(Timestamp, DateTimeOffset.UtcNow.ToString("o"));

        return headers;
    }
}
