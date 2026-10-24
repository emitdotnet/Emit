namespace Emit.Abstractions;

/// <summary>
/// Context of a consumed message, after deserialization: the message along with the
/// <see cref="TransportContext"/> it arrived in. One consume context is created per consumer entry during
/// fan-out. Middleware written for <see cref="ConsumeContext"/> runs on every consumer, whatever its message type.
/// </summary>
public abstract class ConsumeContext : MessageContext
{
    /// <summary>
    /// The parent transport context that produced this consume context.
    /// Provides access to raw bytes, headers, message ID, and provider-specific data.
    /// </summary>
    public required TransportContext TransportContext { get; init; }

    /// <summary>
    /// Message headers. Delegated from <see cref="TransportContext"/>.
    /// </summary>
    public List<KeyValuePair<string, string>> Headers => TransportContext.Headers;

    /// <summary>
    /// Current retry attempt number. Set by <c>RetryMiddleware</c> before each attempt.
    /// </summary>
    public int RetryAttempt { get; set; }

    /// <summary>
    /// The number of messages this context carries: the item count of a batch, otherwise one.
    /// </summary>
    public int MessageCount => Message is IBatchMessage batch ? batch.Count : 1;
}

/// <summary>
/// Context of a consumed message of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The deserialized message type.</typeparam>
public class ConsumeContext<T> : ConsumeContext
{
    /// <summary>
    /// The message being consumed.
    /// </summary>
    public new required T Message
    {
        get => base.Message is T message ? message : default!;
        init => base.Message = value;
    }

    /// <summary>
    /// Builds a per-item <see cref="ConsumeContext{T}"/> from a batch context and one of its
    /// items. Used by batch-aware middleware that needs to evaluate per-item logic (e.g. filters)
    /// against the same surface a single-message consumer would see.
    /// <para>
    /// Per-item state (<see cref="MessageContext.MessageId"/>, <see cref="MessageContext.Timestamp"/>,
    /// <see cref="Message"/>, <see cref="TransportContext"/>, <see cref="MessageContext.SourceAddress"/>)
    /// comes from <paramref name="item"/>. Ambient state (<see cref="MessageContext.CancellationToken"/>,
    /// <see cref="MessageContext.Services"/>, <see cref="MessageContext.DestinationAddress"/>,
    /// <see cref="ConsumeContext.RetryAttempt"/>, and the retry decision) comes from <paramref name="batchContext"/>.
    /// </para>
    /// <para>
    /// MAINTAINER NOTE: when a new property is added to <see cref="MessageContext"/>,
    /// <see cref="ConsumeContext"/>, or <see cref="ConsumeContext{T}"/>, decide whether it is
    /// per-item or ambient and copy it here. This is the canonical construction site for
    /// batch-derived consume contexts.
    /// </para>
    /// </summary>
    /// <param name="batchContext">The parent batch's consume context.</param>
    /// <param name="item">The batch item to lift into a per-item context.</param>
    public static ConsumeContext<T> FromBatchItem(
        ConsumeContext<MessageBatch<T>> batchContext,
        BatchItem<T> item)
    {
        ArgumentNullException.ThrowIfNull(batchContext);
        ArgumentNullException.ThrowIfNull(item);

        return new ConsumeContext<T>
        {
            // MessageContext (base) — ambient from batch, identity from item's transport
            CancellationToken = batchContext.CancellationToken,
            Services = batchContext.Services,
            MessageId = item.TransportContext.MessageId,
            Timestamp = item.TransportContext.Timestamp,
            DestinationAddress = batchContext.DestinationAddress,
            SourceAddress = item.TransportContext.SourceAddress ?? batchContext.SourceAddress,
            WillRetry = batchContext.WillRetry,
            // ConsumeContext: transport per-item, retry ambient
            TransportContext = item.TransportContext,
            RetryAttempt = batchContext.RetryAttempt,
            // ConsumeContext<T>: per-item
            Message = item.Message,
        };
    }
}
