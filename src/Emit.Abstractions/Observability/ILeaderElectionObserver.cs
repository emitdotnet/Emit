namespace Emit.Abstractions.Observability;

/// <summary>
/// Observes leader election and node registration lifecycle events. Implement this
/// interface for monitoring, metrics, or integration test assertions.
/// </summary>
/// <remarks>
/// <para>
/// All methods have default implementations that return <see cref="Task.CompletedTask"/>,
/// so implementors only need to override the callbacks they care about.
/// </para>
/// <para>
/// Observer exceptions are caught and logged individually — a failing observer never
/// blocks other observers or interrupts leader election.
/// </para>
/// </remarks>
public interface ILeaderElectionObserver
{
    /// <summary>
    /// Called when this node has been elected as the leader.
    /// </summary>
    /// <param name="nodeId">The identifier of the node that became leader.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnLeaderElectedAsync(Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when this node has lost the leader role.
    /// </summary>
    /// <param name="nodeId">The identifier of the node that lost leadership.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnLeaderLostAsync(Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when this node has registered itself in the cluster.
    /// </summary>
    /// <param name="nodeId">The identifier of the registered node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnNodeRegisteredAsync(Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when a dead node has been removed from the cluster by the leader.
    /// </summary>
    /// <param name="nodeId">The identifier of the removed node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnNodeRemovedAsync(Guid nodeId, CancellationToken cancellationToken) => Task.CompletedTask;
}
