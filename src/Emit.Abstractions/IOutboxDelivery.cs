namespace Emit.Abstractions;

using Emit.Models;

/// <summary>
/// Delivers a batch of outbox entries. The entries sent through it become visible together when it completes;
/// disposed without completing, it makes none of them visible.
/// </summary>
/// <remarks>
/// A provider whose system cannot hold sends back makes each entry visible when it is sent. Disposing does not wait
/// for the system, so an outbox daemon that is stopped ends within its drain timeout.
/// </remarks>
public interface IOutboxDelivery : IAsyncDisposable
{
    /// <summary>
    /// Hands an entry over to be sent. The entries of a group are handed over one after the other, in order, and
    /// the provider keeps them in that order; entries of different groups can be handed over concurrently.
    /// </summary>
    /// <param name="entry">The entry to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the entry is handed over, not once it is sent.</returns>
    /// <remarks>
    /// The provider reads <see cref="OutboxEntry.Body"/>, <see cref="OutboxEntry.Headers"/> and
    /// <see cref="OutboxEntry.Properties"/> to reconstruct the message. It throws when the entry cannot be handed
    /// over. Whether the entry was sent is only known when the delivery completes.
    /// </remarks>
    Task SendAsync(OutboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes every entry sent through this delivery visible at once.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the entries are visible.</returns>
    /// <remarks>
    /// It throws when they cannot all be made visible, for example because one of them could not be sent or because
    /// a later run of the outbox daemon fenced this one; none of them is then visible. It throws
    /// <see cref="MessageBrokerUnreachableException"/> when the message broker could not be reached: the outbox
    /// daemon then delivers the entries from another node rather than in place.
    /// </remarks>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
