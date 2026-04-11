namespace Emit.Kafka.Tests.TestInfrastructure;

using Emit.Kafka.Observability;

/// <summary>
/// Resolves on the first successful offset commit event that contains an offset
/// for the monitored topic. Used in ordered delivery tests to synchronize on
/// commit completion.
/// </summary>
internal sealed class CommitAwaiter(string topic) : IKafkaConsumerObserver
{
    private readonly TaskCompletionSource<OffsetsCommittedEvent> tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task OnOffsetsCommittedAsync(OffsetsCommittedEvent e)
    {
        if (e.Offsets.Any(o => o.Topic == topic))
        {
            tcs.TrySetResult(e);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits until a commit event for the monitored topic arrives.
    /// </summary>
    public async Task<OffsetsCommittedEvent> WaitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"No offset commit for topic '{topic}' received within {timeout}.");
        }
    }
}
