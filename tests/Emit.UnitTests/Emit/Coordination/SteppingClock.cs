namespace Emit.UnitTests.Coordination;

using Microsoft.Extensions.Time.Testing;

/// <summary>
/// Fake clock whose wall-clock time can be stepped, as clock synchronization does or as a system suspend appears,
/// while monotonic timestamps and timers keep advancing only with <see cref="FakeTimeProvider.Advance"/>.
/// </summary>
internal class SteppingClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private TimeSpan offset;

    public void StepWallClock(TimeSpan step) => offset += step;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + offset;

    public override long GetTimestamp() => base.GetUtcNow().UtcTicks;
}
