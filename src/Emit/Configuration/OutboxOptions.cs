namespace Emit.Configuration;

/// <summary>
/// Configuration options for the transactional outbox.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets the interval at which the outbox polls for new entries.
    /// </summary>
    /// <remarks>
    /// When the outbox is empty or a batch completes, the daemon waits this duration
    /// before checking again. Minimum value: 1 second.
    /// </remarks>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of entries to process in a single batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of entry groups dispatched in parallel within a single batch.
    /// </summary>
    /// <remarks>
    /// Entries are partitioned by group key. Each group is processed sequentially to preserve
    /// ordering, while distinct groups are dispatched concurrently up to this limit. Higher values
    /// increase throughput at the cost of more concurrent load on the message broker.
    /// </remarks>
    public int MaxConcurrentGroups { get; set; } = 32;

}
