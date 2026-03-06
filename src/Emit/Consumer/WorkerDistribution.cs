namespace Emit.Consumer;

/// <summary>
/// Determines how the poll loop routes messages to workers in the pool.
/// </summary>
public enum WorkerDistribution
{
    /// <summary>
    /// Messages with the same key always go to the same worker.
    /// Preserves key-level ordering. Default.
    /// </summary>
    ByKeyHash,

    /// <summary>
    /// Messages distributed evenly across workers regardless of key.
    /// No ordering guarantees. Maximum throughput.
    /// </summary>
    RoundRobin
}
