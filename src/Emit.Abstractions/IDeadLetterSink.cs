namespace Emit.Abstractions;

/// <summary>
/// Produces messages to a dead letter destination when error handling determines
/// that a message cannot be processed. The caller constructs appropriate headers
/// and provides raw message bytes to avoid re-serialization.
/// </summary>
public interface IDeadLetterSink
{
    /// <summary>
    /// The transport-agnostic destination address where dead-lettered messages are produced.
    /// </summary>
    Uri DestinationAddress { get; }

    /// <summary>
    /// Sends a message to the dead letter destination.
    /// </summary>
    /// <param name="rawKey">The raw message key bytes.</param>
    /// <param name="rawValue">The raw message value bytes.</param>
    /// <param name="headers">
    /// Headers to include on the dead letter message, including both original message headers
    /// and diagnostic headers added by the error handling pipeline.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message has been produced.</returns>
    Task ProduceAsync(
        byte[]? rawKey,
        byte[]? rawValue,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken);
}
