namespace Emit.Abstractions;
/// <summary>
/// Handles consumed messages for a specific message type.
/// Implementations must be idempotent.
/// </summary>
/// <typeparam name="TValue">The message value type.</typeparam>
public interface IConsumer<TValue>
{
    /// <summary>
    /// Processes a single consumed message.
    /// </summary>
    /// <param name="context">The inbound context carrying the message and pipeline metadata.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    Task ConsumeAsync(InboundContext<TValue> context, CancellationToken cancellationToken);
}
