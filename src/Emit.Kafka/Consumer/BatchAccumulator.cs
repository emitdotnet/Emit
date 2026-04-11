namespace Emit.Kafka.Consumer;

using System.Threading.Channels;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Two-phase batch accumulator. Phase 1 blocks indefinitely until at least one message
/// arrives. Phase 2 accumulates up to <c>maxSize</c> or until <c>timeout</c> elapses,
/// whichever comes first. The timeout starts counting from when the first message arrives,
/// not from when the worker starts waiting.
/// </summary>
internal sealed class BatchAccumulator(
    int maxSize,
    TimeSpan timeout,
    ChannelReader<ConfluentKafka.ConsumeResult<byte[], byte[]>> reader)
{
    /// <summary>
    /// Accumulates up to <c>maxSize</c> messages or until <c>timeout</c>
    /// elapses after the first message arrives. Returns <c>null</c> if the channel completes
    /// with no messages accumulated.
    /// </summary>
    public async Task<List<ConfluentKafka.ConsumeResult<byte[], byte[]>>?> AccumulateAsync(
        CancellationToken cancellationToken)
    {
        // Phase 1: Wait for at least one message (blocks indefinitely)
        if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var batch = new List<ConfluentKafka.ConsumeResult<byte[], byte[]>>(maxSize);

        // Drain whatever is immediately available
        while (batch.Count < maxSize && reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count >= maxSize)
            return batch;

        // Phase 2: Accumulate more messages up to maxSize or timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (batch.Count < maxSize)
            {
                if (!await reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
                    break; // channel completed

                while (batch.Count < maxSize && reader.TryRead(out var item))
                {
                    batch.Add(item);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                  && !cancellationToken.IsCancellationRequested)
        {
            // Timeout elapsed — dispatch what we have (partial batch)
        }

        return batch.Count > 0 ? batch : null;
    }
}
