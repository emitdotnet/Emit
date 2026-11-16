namespace Emit.EntityFrameworkCore.Configuration;

/// <summary>
/// Configuration options for the removal of expired distributed locks.
/// </summary>
public sealed class LockCleanupOptions
{
    /// <summary>
    /// Gets or sets how often expired locks are removed. Default: 5 minutes.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}
