namespace Emit.EntityFrameworkCore.Configuration;

/// <summary>
/// Configuration options for the Entity Framework Core inbox.
/// </summary>
public sealed class EntityFrameworkCoreInboxOptions
{
    /// <summary>
    /// Gets or sets how long a handled message stays recorded. A copy that arrives later is handled again.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how often expired records are removed. Default: 1 minute.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the most records removed in one database call. Default: 1000.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 1000;
}
