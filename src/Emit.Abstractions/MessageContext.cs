namespace Emit.Abstractions;

/// <summary>
/// Base context for all pipeline operations. Provides cancellation, service resolution,
/// a typed payload bag, and message identity. Each pattern (Kafka, Mediator, Bus) provides
/// a subclass via <see cref="MessageContext{T}"/> that carries strongly-typed message data.
/// </summary>
public abstract class MessageContext
{
    private Dictionary<Type, object>? payloads;

    /// <summary>
    /// Cancellation token for this processing operation.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Scoped service provider for this processing operation.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Feature collection for optional, pattern-specific capabilities.
    /// </summary>
    public IFeatureCollection Features { get; } = new FeatureCollection();

    /// <summary>
    /// Unique identifier for this message processing operation.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// When the message was received or created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The address where this message is being sent to or was received from.
    /// Built as a transport URI (e.g. <c>kafka://broker:9092/my-topic</c>).
    /// </summary>
    public Uri? DestinationAddress { get; set; }

    /// <summary>
    /// The address of the sender. On produce, set to the broker host address.
    /// On consume, extracted from headers if the producer injected it.
    /// </summary>
    public Uri? SourceAddress { get; set; }

    /// <summary>
    /// Retrieves an optional payload of type <typeparamref name="T"/> from the context.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <returns>The payload instance, or <c>null</c> if not set.</returns>
    public T? TryGetPayload<T>() where T : class
    {
        if (payloads is not null && payloads.TryGetValue(typeof(T), out var value))
        {
            return (T)value;
        }

        return null;
    }

    /// <summary>
    /// Sets a payload of type <typeparamref name="T"/> on the context. Overwrites any existing
    /// payload of the same type.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="payload">The payload instance.</param>
    public void SetPayload<T>(T payload) where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        payloads ??= [];
        payloads[typeof(T)] = payload;
    }
}

/// <summary>
/// Typed message context that provides strongly-typed access to the message payload.
/// All runtime pipelines operate on this typed context — there is no non-generic pipeline
/// at runtime. Global middleware uses open generics closed per message type at build time.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class MessageContext<T> : MessageContext
{
    /// <summary>
    /// The message payload being processed.
    /// </summary>
    public required T Message { get; init; }
}
