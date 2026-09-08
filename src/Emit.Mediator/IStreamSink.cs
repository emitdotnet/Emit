namespace Emit.Mediator;

/// <summary>
/// Carries the items a stream handler produces out of the pipeline to the caller enumerating
/// the stream. Placed on the context features by the dispatcher.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the produced sequence.</typeparam>
/// <remarks>
/// The handler is advanced only when the consumer asks for an item, so it is always parked at
/// one of its own yield points while the consumer is between items. That is what lets a
/// consumer stop early without interrupting the handler mid-operation.
/// </remarks>
internal interface IStreamSink<in TResponse>
{
    /// <summary>
    /// Waits until the consumer asks for the next item.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    /// <returns>
    /// <see langword="true"/> when an item is wanted, or <see langword="false"/> when the
    /// consumer has stopped reading. Stopping this way is a normal end to the request, not a
    /// failure, so the pipeline unwinds successfully.
    /// </returns>
    ValueTask<bool> WaitForDemandAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Hands over the item the consumer asked for. Only valid after
    /// <see cref="WaitForDemandAsync"/> returned <see langword="true"/>.
    /// </summary>
    /// <param name="item">The item to hand over.</param>
    /// <param name="cancellationToken">Cancellation token for this operation.</param>
    ValueTask WriteAsync(TResponse item, CancellationToken cancellationToken);
}
