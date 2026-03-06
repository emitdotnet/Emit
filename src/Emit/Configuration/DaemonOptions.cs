namespace Emit.Configuration;

/// <summary>
/// Configuration options for daemon assignment coordination.
/// </summary>
public sealed class DaemonOptions
{
    /// <summary>
    /// Gets or sets how long a node has to acknowledge an assignment before the leader
    /// considers it stuck and reassigns.
    /// </summary>
    public TimeSpan AcknowledgeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a node has to drain (stop) a revoked daemon before the leader
    /// force-reassigns it.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
