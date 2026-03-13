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

}
