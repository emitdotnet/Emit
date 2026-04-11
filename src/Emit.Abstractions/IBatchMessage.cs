namespace Emit.Abstractions;

/// <summary>
/// Marker interface implemented by <see cref="MessageBatch{T}"/> to enable batch-aware
/// behavior in existing middleware without requiring batch-specific middleware variants.
/// Middleware checks <c>context.Message is IBatchMessage</c> for per-item dead-lettering,
/// rate limit permit counting, and metrics normalization.
/// </summary>
public interface IBatchMessage
{
    /// <summary>
    /// The number of items in the batch.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Yields the transport context for each item in the batch.
    /// Used by <c>ConsumeErrorMiddleware</c> for per-item dead-lettering.
    /// Each item's existing <see cref="TransportContext"/> carries RawKey, RawValue, and Headers.
    /// </summary>
    IEnumerable<TransportContext> GetItemTransportContexts();
}
