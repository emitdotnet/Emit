namespace Emit.Abstractions;

/// <summary>
/// Controls the flow of messages from a consumer by pausing and resuming consumption.
/// Used by circuit breakers and other protective mechanisms to temporarily halt
/// message delivery while keeping the consumer connected.
/// </summary>
public interface IConsumerFlowControl
{
    /// <summary>
    /// Temporarily halts message consumption while keeping the consumer connected to the broker.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when consumption has been paused.</returns>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resumes message consumption after a previous pause.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when consumption has been resumed.</returns>
    Task ResumeAsync(CancellationToken cancellationToken);
}
