namespace Emit.Kafka.Consumer;

/// <summary>
/// Batch accumulation configuration for a consumer group.
/// Accumulation happens per-worker: each worker in the consumer group independently
/// accumulates messages dispatched to it by the distribution strategy. The actual batch
/// size depends on message arrival rate and worker count, and may be smaller than
/// <see cref="MaxSize"/> even when more messages are available across other workers.
/// </summary>
public sealed class BatchConfig
{
    /// <summary>
    /// Maximum number of messages per worker batch. Default: 100.
    /// Each worker accumulates up to this many messages independently.
    /// </summary>
    public int MaxSize { get; set; } = 100;

    /// <summary>
    /// Maximum time to wait after the first message before dispatching a partial batch. Default: 5 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
