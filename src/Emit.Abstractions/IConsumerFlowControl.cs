namespace Emit.Abstractions;

/// <summary>
/// Pauses and resumes a consumer group declaration on this node while its consumer stays in the group.
/// </summary>
/// <remarks>
/// <para>
/// Resolve it as a keyed service under the name of the consumer group declaration. The pause applies to this
/// node only, holds through rebalances, and resumes from where consumption stopped. It is kept in memory: a
/// restarted process starts with the group running.
/// </para>
/// <para>
/// A circuit breaker pauses the group on its own. A group paused by both stays paused until both resumed it:
/// resuming never cuts an open circuit short, and a circuit closing never resumes a group the application paused.
/// </para>
/// </remarks>
public interface IConsumerFlowControl
{
    /// <summary>
    /// Pauses the group. Calling it while the group is already paused does nothing.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the messages being handled. The group stays paused.</param>
    /// <returns>
    /// A task that completes once no message of the group is fetched or being handled on this node: messages
    /// already handed to its handlers finish first. Called from a handler of the same group, it completes without
    /// waiting for them, since one of them is the caller.
    /// </returns>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resumes the group from where it stopped. Calling it while the group is not paused does nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the group is resumed, or still held by an open circuit.</returns>
    Task ResumeAsync(CancellationToken cancellationToken);
}
