namespace Emit.Kafka.Consumer;

/// <summary>
/// Routes messages by hashing key bytes. Preserves key-level ordering.
/// </summary>
internal sealed class ByKeyHashStrategy(int workerCount) : IDistributionStrategy
{
    private readonly int workerCount = workerCount;

    /// <inheritdoc />
    public int SelectWorker(byte[]? keyBytes, int partition)
    {
        if (keyBytes is null or { Length: 0 })
        {
            return 0;
        }

        var sum = 0;
        foreach (var b in keyBytes)
        {
            sum += b;
        }

        return (sum & 0x7FFFFFFF) % workerCount;
    }
}
