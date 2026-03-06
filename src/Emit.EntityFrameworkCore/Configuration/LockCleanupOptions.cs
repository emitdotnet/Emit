namespace Emit.EntityFrameworkCore.Configuration;

/// <summary>
/// Configuration options for the distributed lock cleanup worker.
/// </summary>
public sealed class LockCleanupOptions
{
    /// <summary>
    /// Gets or sets the interval at which expired locks are cleaned up.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}
