namespace Emit.Kafka.Consumer;

using Emit.Abstractions;
using Microsoft.Extensions.Logging;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific implementation of <see cref="IConsumerFlowControl"/> that pauses and resumes
/// partition consumption while maintaining consumer group membership. When paused, the
/// consumer's poll loop continues to call <c>Consume()</c> which sends heartbeats to the broker,
/// preventing <c>max.poll.interval.ms</c> timeout.
/// </summary>
internal sealed class KafkaConsumerFlowControl(ILogger<KafkaConsumerFlowControl> logger) : IConsumerFlowControl, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool paused;
    private ConfluentKafka.IConsumer<byte[], byte[]>? consumer;

    /// <summary>
    /// Whether the consumer is currently paused.
    /// </summary>
    internal bool IsPaused => paused;

    /// <summary>
    /// Sets the underlying Confluent consumer instance. Called by the consumer group worker
    /// after the consumer is built on the background thread.
    /// </summary>
    internal void SetConsumer(ConfluentKafka.IConsumer<byte[], byte[]> consumer)
    {
        this.consumer = consumer;
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (paused)
            {
                return;
            }

            paused = true;
            PauseAssignedPartitions();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!paused)
            {
                return;
            }

            paused = false;
            ResumeAssignedPartitions();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Pauses newly assigned partitions if the consumer is currently in paused state.
    /// Called by the consumer group worker during partition assignment to ensure
    /// new partitions respect the current flow control state.
    /// </summary>
    internal void PauseIfNeeded(IEnumerable<ConfluentKafka.TopicPartition> partitions)
    {
        if (!paused || consumer is null)
        {
            return;
        }

        var list = partitions.ToList();
        if (list.Count > 0)
        {
            consumer.Pause(list);
            logger.LogDebug("Auto-paused {Count} newly assigned partitions (flow control is active)", list.Count);
        }
    }

    private void PauseAssignedPartitions()
    {
        if (consumer is null)
        {
            return;
        }

        var assignment = consumer.Assignment;
        if (assignment.Count > 0)
        {
            consumer.Pause(assignment);
            logger.LogInformation("Paused {Count} partitions", assignment.Count);
        }
    }

    private void ResumeAssignedPartitions()
    {
        if (consumer is null)
        {
            return;
        }

        var assignment = consumer.Assignment;
        if (assignment.Count > 0)
        {
            consumer.Resume(assignment);
            logger.LogInformation("Resumed {Count} partitions", assignment.Count);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }
}
