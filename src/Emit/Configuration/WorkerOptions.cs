namespace Emit.Configuration;

/// <summary>
/// Configuration options for the outbox worker.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>
    /// Gets or sets the duration of the global lease.
    /// </summary>
    /// <remarks>
    /// The lease prevents multiple workers from processing the outbox simultaneously.
    /// Minimum value: 30 seconds.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the interval at which the lease is renewed.
    /// </summary>
    /// <remarks>
    /// Must be significantly less than <see cref="LeaseDuration"/> to ensure
    /// uninterrupted processing. Recommended: less than half of LeaseDuration.
    /// Minimum value: 5 seconds.
    /// </remarks>
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets a unique identifier for this worker instance.
    /// </summary>
    /// <remarks>
    /// If not specified, a unique identifier is generated automatically.
    /// Useful for debugging and logging to identify which worker holds the lease.
    /// </remarks>
    public string? WorkerId { get; set; }
}
