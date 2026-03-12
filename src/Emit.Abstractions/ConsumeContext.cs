namespace Emit.Abstractions;

/// <summary>
/// Post-deserialization consume context. Carries the typed message along with a
/// reference to the parent <see cref="TransportContext"/>. One consume context is
/// created per consumer entry during fan-out.
/// </summary>
/// <typeparam name="T">The deserialized message type.</typeparam>
public class ConsumeContext<T> : MessageContext<T>
{
    /// <summary>
    /// The parent transport context that produced this consume context.
    /// Provides access to raw bytes, headers, message ID, and provider-specific data.
    /// </summary>
    public required TransportContext TransportContext { get; init; }

    /// <summary>
    /// Message headers. Delegated from <see cref="TransportContext"/>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers => TransportContext.Headers;

    /// <summary>
    /// Current retry attempt number. Set by <c>RetryMiddleware</c> before each attempt.
    /// </summary>
    public int RetryAttempt { get; set; }

    /// <summary>
    /// Transaction context for transactional consumers. Set by transaction middleware.
    /// </summary>
    public ITransactionContext? Transaction { get; set; }
}
