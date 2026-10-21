namespace Emit.LeaderElection;

/// <summary>
/// An instant read from the monotonic clock and from the wall clock together. The time elapsed since it is the
/// longer of the two readings: the monotonic clock does not advance while the system is suspended, and the wall
/// clock can be stepped back, but as long as either counts the time that passed, a lease measured from this instant
/// does not last longer.
/// </summary>
/// <remarks>
/// Only for this node's own deadlines, where counting too much time is safe. Time this node waits on another node
/// is measured on the monotonic clock alone, which never counts more time than has passed. A virtual machine paused
/// so that neither of its clocks counts the pause cannot tell; its next database read shows what changed meanwhile.
/// </remarks>
/// <param name="Timestamp">The monotonic timestamp.</param>
/// <param name="UtcNow">The wall-clock time.</param>
internal readonly record struct ClockReading(long Timestamp, DateTimeOffset UtcNow)
{
    /// <summary>
    /// Reads both clocks of <paramref name="timeProvider"/>.
    /// </summary>
    public static ClockReading Now(TimeProvider timeProvider) => new(timeProvider.GetTimestamp(), timeProvider.GetUtcNow());

    /// <summary>
    /// Returns the time elapsed since this reading.
    /// </summary>
    public TimeSpan Elapsed(TimeProvider timeProvider)
    {
        var monotonic = timeProvider.GetElapsedTime(Timestamp);
        var wall = timeProvider.GetUtcNow() - UtcNow;
        return wall > monotonic ? wall : monotonic;
    }
}
