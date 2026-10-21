namespace Emit.UnitTests.Coordination;

using global::Emit.LeaderElection;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Node leases for tests where the lease is not what is tested, or ran out in a particular way.
/// </summary>
internal static class Leases
{
    /// <summary>
    /// Returns a lease that never runs out.
    /// </summary>
    public static LeaseTokenSource Held() =>
        new(TimeProvider.System, InlineTaskScheduler.Instance, TimeSpan.FromSeconds(1), NullLogger.Instance);

    /// <summary>
    /// Returns a lease on <paramref name="clock"/> that runs out <paramref name="after"/> from now.
    /// </summary>
    public static LeaseTokenSource ExpiringAfter(SteppingClock clock, TimeSpan after)
    {
        var lease = new LeaseTokenSource(clock, InlineTaskScheduler.Instance, TimeSpan.FromSeconds(1), NullLogger.Instance);
        lease.ExpireAt(ClockReading.Now(clock), after);
        return lease;
    }

    /// <summary>
    /// Returns a lease that ran out while the system was suspended: the wall clock moved past it, but the
    /// monotonic clock and the timers did not, so its token is not cancelled yet.
    /// </summary>
    public static LeaseTokenSource LostInASuspend(SteppingClock clock)
    {
        var lease = ExpiringAfter(clock, TimeSpan.FromMinutes(1));
        clock.StepWallClock(TimeSpan.FromMinutes(1));
        return lease;
    }
}
