namespace Emit.Kafka.Consumer;

/// <summary>
/// Selects which worker should process a given message.
/// </summary>
internal interface IDistributionStrategy
{
    /// <summary>
    /// Returns the zero-based worker index that should process this message.
    /// </summary>
    int SelectWorker(byte[]? keyBytes, int partition);
}
