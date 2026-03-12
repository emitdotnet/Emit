namespace Emit.Configuration;

/// <summary>
/// Shared constants for configuration validation.
/// </summary>
public static class ValidationConstants
{
    /// <summary>
    /// Maximum allowed batch size for outbox operations.
    /// </summary>
    public const int MaxBatchSize = 10000;

    /// <summary>
    /// Minimum allowed polling interval.
    /// </summary>
    public static readonly TimeSpan MinPollingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum allowed lock cleanup interval.
    /// </summary>
    public static readonly TimeSpan MinLockCleanupInterval = TimeSpan.FromMinutes(1);
}
