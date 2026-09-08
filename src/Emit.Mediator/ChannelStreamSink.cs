namespace Emit.Mediator;

using System.Threading.Channels;

/// <summary>
/// Pairs a demand channel with an item channel so the handler advances exactly one item per
/// request from the consumer.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the produced sequence.</typeparam>
/// <remarks>
/// Completing the demand channel is how the consumer says it has stopped reading. Because the
/// handler is only ever advanced in response to demand, it is parked at one of its own yield
/// points when that happens, so it stops at a point it chose rather than being interrupted.
/// </remarks>
internal sealed class ChannelStreamSink<TResponse>(
    ChannelReader<bool> demand,
    ChannelWriter<TResponse> items) : IStreamSink<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<bool> WaitForDemandAsync(CancellationToken cancellationToken)
    {
        while (await demand.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (demand.TryRead(out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(TResponse item, CancellationToken cancellationToken)
        => items.WriteAsync(item, cancellationToken);
}
