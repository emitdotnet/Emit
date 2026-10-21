namespace Emit.Abstractions.Daemon;

/// <summary>
/// The assignment a run of a daemon belongs to.
/// </summary>
public sealed record DaemonRunContext
{
    private readonly Func<bool> holdsAssignment;

    /// <summary>
    /// Creates the context of a run.
    /// </summary>
    /// <param name="assignmentToken">The token cancelled when the daemon must stop.</param>
    /// <param name="generation">The generation of the assignment.</param>
    /// <param name="fencingKey">The key identifying this daemon of this service in this database.</param>
    /// <param name="holdsAssignment">Returns whether the run still holds its assignment at the moment it is called.</param>
    public DaemonRunContext(CancellationToken assignmentToken, long generation, Guid fencingKey, Func<bool> holdsAssignment)
    {
        ArgumentNullException.ThrowIfNull(holdsAssignment);

        AssignmentToken = assignmentToken;
        Generation = generation;
        FencingKey = fencingKey;
        this.holdsAssignment = holdsAssignment;
    }

    /// <summary>
    /// Gets the token cancelled when the daemon must stop. The daemon must stop all work promptly when it fires,
    /// within the drain timeout: while its node keeps heartbeating, the daemon is not started again or moved to
    /// another node until its run completes, but a node cut off from the database has its daemons moved once its
    /// registration expires. The token is cancelled on the thread pool, so it can fire late when the pool is busy;
    /// <see cref="HoldsAssignment"/> does not depend on it.
    /// </summary>
    public CancellationToken AssignmentToken { get; init; }

    /// <summary>
    /// Gets the generation of the assignment. Every later assignment of the daemon has a higher one, so a system the
    /// daemon writes to can refuse the writes of an earlier run that is still going, for example after a long pause.
    /// </summary>
    public long Generation { get; init; }

    /// <summary>
    /// Gets the key identifying this daemon of this service in this database, the same for every run of it.
    /// Together with <see cref="Generation"/>, it lets a system the daemon writes to fence earlier runs.
    /// </summary>
    public Guid FencingKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run still holds its assignment, read at the moment it is called: it is
    /// <c>false</c> as soon as the run is asked to stop, or as soon as the node's lease the run started under could
    /// have run out, even before <see cref="AssignmentToken"/> is cancelled, as after a pause, a suspend or on a busy
    /// thread pool. Read it right before each side effect that must not overlap with another node's run.
    /// </summary>
    public bool HoldsAssignment => !AssignmentToken.IsCancellationRequested && holdsAssignment();
}
