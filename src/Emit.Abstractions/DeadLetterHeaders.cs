namespace Emit.Abstractions;

/// <summary>
/// Well-known header name constants and factory methods for dead-letter messages.
/// </summary>
public static class DeadLetterHeaders
{
    /// <summary>The fully qualified exception type name.</summary>
    public const string ExceptionType = "x-emit-exception-type";

    /// <summary>The exception message.</summary>
    public const string ExceptionMessage = "x-emit-exception-message";

    /// <summary>The number of processing attempts made before dead-lettering.</summary>
    public const string RetryCount = "x-emit-retry-count";

    /// <summary>The UTC timestamp when the message was dead-lettered, in ISO 8601 round-trip format.</summary>
    public const string Timestamp = "x-emit-timestamp";

    /// <summary>The consumer group that failed to process the message.</summary>
    public const string ConsumerGroup = "x-emit-consumer-group";

    /// <summary>The consumer handler identifier that failed to process the message.</summary>
    public const string Consumer = "x-emit-consumer";

    /// <summary>The fully qualified type name of the consumer that failed to process the message.</summary>
    public const string ConsumerType = "x-emit-consumer-type";

    /// <summary>The route key matched when dispatching the message to the consumer.</summary>
    public const string RouteKey = "x-emit-route-key";

    /// <summary>The W3C traceparent from the original message, preserved for distributed trace replay linking.</summary>
    public const string OriginalTraceParent = "emit.dlq.original_traceparent";

    /// <summary>The source topic the message was originally consumed from.</summary>
    public const string OriginalTopic = "emit.dlq.original_topic";

    /// <summary>Prefix applied to headers copied from transport source properties.</summary>
    public const string SourcePrefix = "x-emit-source-";

    /// <summary>
    /// Creates a base dead-letter header list by preserving original message headers and appending
    /// standard diagnostic headers derived from the exception and source properties.
    /// </summary>
    /// <param name="originalHeaders">The headers from the original message, or <c>null</c> if unavailable.</param>
    /// <param name="exceptionType">The type of the exception that caused the dead-letter.</param>
    /// <param name="exceptionMessage">The exception message.</param>
    /// <param name="sourceProperties">The transport source properties, or <c>null</c> if unavailable.</param>
    /// <returns>A mutable list of headers ready for caller-specific headers to be appended.</returns>
    public static List<KeyValuePair<string, string>> CreateBase(
        IReadOnlyList<KeyValuePair<string, string>>? originalHeaders,
        Type exceptionType,
        string exceptionMessage,
        IReadOnlyDictionary<string, string>? sourceProperties)
    {
        var headers = new List<KeyValuePair<string, string>>();

        if (originalHeaders is not null)
        {
            headers.AddRange(originalHeaders);
        }

        var traceparent = originalHeaders?.FirstOrDefault(h => h.Key == WellKnownHeaders.TraceParent).Value;
        if (!string.IsNullOrEmpty(traceparent))
        {
            headers.Add(new(OriginalTraceParent, traceparent));
        }

        var sourceTopic = sourceProperties?.GetValueOrDefault("topic");
        if (!string.IsNullOrEmpty(sourceTopic))
        {
            headers.Add(new(OriginalTopic, sourceTopic));
        }

        headers.Add(new(ExceptionType, exceptionType.FullName ?? exceptionType.Name));
        headers.Add(new(ExceptionMessage, exceptionMessage));

        if (sourceProperties is not null)
        {
            foreach (var prop in sourceProperties)
            {
                headers.Add(new($"{SourcePrefix}{prop.Key}", prop.Value));
            }
        }

        headers.Add(new(Timestamp, DateTimeOffset.UtcNow.ToString("o")));

        return headers;
    }
}
