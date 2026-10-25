namespace Emit.Abstractions;

/// <summary>
/// Base context for all pipeline operations: the message, its identity, cancellation, service resolution and a
/// payload bag. Each kind of pipeline has its own context derived from it: <see cref="ConsumeContext"/> for a
/// message consumed, <see cref="SendContext"/> for a message produced, and the mediator's context for a request.
/// Middleware written for <see cref="MessageContext"/> runs on every pipeline.
/// </summary>
public abstract class MessageContext
{
    private Dictionary<Type, object>? payloads;
    private IServiceProvider services = null!;
    private object? message;

    /// <summary>
    /// Cancellation token for this processing operation.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Scoped service provider for this processing operation.
    /// </summary>
    public required IServiceProvider Services
    {
        get => services;
        init => services = value;
    }

    /// <summary>
    /// Feature collection for optional, pattern-specific capabilities.
    /// </summary>
    public IFeatureCollection Features { get; } = new FeatureCollection();

    /// <summary>
    /// The message being processed. The typed context of each pipeline exposes it with its type.
    /// </summary>
    public object? Message
    {
        get => message;
        protected init => message = value;
    }

    /// <summary>
    /// The identity of the message. On consume it is the same on every delivery of the message (after a
    /// restart or a rebalance, or a redelivery from the outbox), which makes it the key to deduplicate on. A
    /// message produced outside Emit is identified by where it is in the broker, also the same on every
    /// delivery. A batch has its own identifier; each of its items has the identity of its message.
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
    /// Decides whether a failure of the current attempt will be followed by another attempt.
    /// Set by the retry middleware; <see langword="null"/> when nothing retries this operation,
    /// in which case every failure is final.
    /// </summary>
    internal Func<Exception, bool>? WillRetry { get; set; }

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

    /// <summary>
    /// Replaces the service provider on this context. Used by the retry middleware
    /// to give each attempt fresh scoped services.
    /// </summary>
    internal void WithServices(IServiceProvider services)
    {
        this.services = services;
    }

    /// <summary>
    /// Replaces the message on this context. Used by batch middleware to replace the batch with a filtered subset
    /// without losing <see cref="Features"/> or <see cref="Services"/>.
    /// </summary>
    internal void WithMessage(object? message)
    {
        this.message = message;
    }
}

