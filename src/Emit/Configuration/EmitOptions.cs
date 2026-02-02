namespace Emit.Configuration;

/// <summary>
/// Base configuration options for Emit.
/// </summary>
public sealed class EmitOptions
{
    /// <summary>
    /// Gets or sets the interval at which the outbox worker polls for new entries.
    /// </summary>
    /// <remarks>
    /// When the outbox is empty or a batch completes, the worker waits this duration
    /// before checking again. Minimum value: 1 second.
    /// </remarks>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of entries to process in a single batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of group heads to query per polling cycle.
    /// </summary>
    public int MaxGroupsPerCycle { get; set; } = 1000;
}
