namespace Emit.Kafka.Consumer;

/// <summary>
/// Distributes messages evenly across workers in round-robin order.
/// </summary>
internal sealed class RoundRobinStrategy(int workerCount) : IDistributionStrategy
{
    private readonly int workerCount = workerCount;
    private int counter = -1;

    /// <inheritdoc />
    public int SelectWorker(byte[]? keyBytes, int partition)
    {
        return (Interlocked.Increment(ref counter) & 0x7FFFFFFF) % workerCount;
    }
}
