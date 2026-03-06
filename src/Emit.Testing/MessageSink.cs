namespace Emit.Testing;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Emit.Abstractions;

/// <summary>
/// Collects consumed messages in order for test assertions. Register as a singleton
/// in the test's service collection and pair with <see cref="SinkConsumer{T}"/>
/// to capture messages delivered through the consumer pipeline.
/// </summary>
/// <typeparam name="T">The message value type.</typeparam>
public sealed class MessageSink<T>
{
    private readonly Channel<InboundContext<T>> channel = Channel.CreateUnbounded<InboundContext<T>>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });

    private readonly ConcurrentQueue<InboundContext<T>> received = new();

    /// <summary>
    /// All messages received so far, in the order they were consumed.
    /// </summary>
    public IReadOnlyCollection<InboundContext<T>> ReceivedMessages => received;

    /// <summary>
    /// Writes a consumed message to the sink. Called by <see cref="SinkConsumer{T}"/>.
    /// </summary>
    /// <param name="context">The inbound context carrying the consumed message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message has been recorded.</returns>
    public async Task WriteAsync(InboundContext<T> context, CancellationToken cancellationToken)
    {
        received.Enqueue(context);
        await channel.Writer.WriteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for the next unconsumed message to arrive within the specified timeout.
    /// Each call returns the next message in order; messages are not replayed.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for a message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inbound context carrying the next consumed message.</returns>
    /// <exception cref="TimeoutException">No message arrived within the timeout.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token was triggered.</exception>
    public async Task<InboundContext<T>> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No message was received within the timeout of {timeout}.");
        }
    }
}
