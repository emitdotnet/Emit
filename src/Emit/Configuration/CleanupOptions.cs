namespace Emit.Configuration;

/// <summary>
/// Configuration options for the completed entries cleanup task.
/// </summary>
public sealed class CleanupOptions
{
    /// <summary>
    /// Gets or sets how long completed entries are retained before deletion.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the interval at which the cleanup task runs.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of entries to delete per cleanup cycle.
    /// </summary>
    public int BatchSize { get; set; } = 1000;
}
