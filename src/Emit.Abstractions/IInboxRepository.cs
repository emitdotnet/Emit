namespace Emit.Abstractions;

/// <summary>
/// Records the messages each consumer handled, so a message that arrives again can be skipped.
/// </summary>
/// <remarks>
/// Implemented by persistence providers. Records are written in the transaction of the current unit of work, from
/// <see cref="IEmitContext.Transaction"/>, so they commit or roll back with the consumer's own writes.
/// </remarks>
public interface IInboxRepository
{
    /// <summary>
    /// Records that a handler handled a message, unless it is already recorded.
    /// </summary>
    /// <param name="handler">Identifies the consumer that handles the message.</param>
    /// <param name="messageId">The message id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the message was recorded now; <see langword="false"/> when the handler already
    /// handled it.
    /// </returns>
    /// <exception cref="InvalidOperationException">No unit of work is active.</exception>
    Task<bool> TryRecordAsync(string handler, string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a handler handled several messages in one database call, skipping those already recorded.
    /// </summary>
    /// <param name="handler">Identifies the consumer that handles the messages.</param>
    /// <param name="messageIds">The message ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message ids recorded now.</returns>
    /// <exception cref="InvalidOperationException">No unit of work is active.</exception>
    Task<IReadOnlySet<string>> RecordAsync(
        string handler,
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default);
}
