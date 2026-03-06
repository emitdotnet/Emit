namespace Emit.Abstractions;
/// <summary>
/// The non-generic envelope that flows through the middleware pipeline.
/// Each pattern (Kafka, Mediator, Bus) provides an internal subclass via
/// <see cref="MessageContext{T}"/> that carries strongly-typed message data.
/// </summary>
public abstract class MessageContext
{
    /// <summary>
    /// Unique identifier for this message processing operation.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// When the message was received or created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Cancellation token for this processing operation.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Scoped service provider for this message processing operation.
    /// Available for middleware that needs to resolve scoped services.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Feature collection for optional, pattern-specific capabilities.
    /// </summary>
    public IFeatureCollection Features { get; } = new FeatureCollection();
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
