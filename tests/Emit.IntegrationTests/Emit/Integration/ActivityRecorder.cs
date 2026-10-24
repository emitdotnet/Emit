namespace Emit.IntegrationTests.Integration;

using System.Collections.Concurrent;
using System.Diagnostics;
using Emit.Abstractions.Tracing;

/// <summary>
/// Records every span Emit's sources finish. The listener is process-wide, so a test finds its own spans through
/// the topics only it uses, or through its own trace.
/// </summary>
public sealed class ActivityRecorder : IDisposable
{
    private readonly ConcurrentQueue<Activity> stopped = new();
    private readonly ActivityListener listener;

    public ActivityRecorder()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is
                EmitActivitySourceNames.Producer or EmitActivitySourceNames.Consumer or EmitActivitySourceNames.Mediator,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
    }

    /// <summary>
    /// Returns the finished spans named <paramref name="name"/>.
    /// </summary>
    public IReadOnlyList<Activity> Named(string name) => [.. stopped.Where(a => a.DisplayName == name)];

    /// <summary>
    /// Returns the finished spans whose destination is <paramref name="destination"/>.
    /// </summary>
    public IReadOnlyList<Activity> OfDestination(string destination) =>
        [.. stopped.Where(a => (string?)a.GetTagItem("messaging.destination.name") == destination)];

    /// <summary>
    /// Returns the finished children of <paramref name="parent"/>.
    /// </summary>
    public IReadOnlyList<Activity> ChildrenOf(Activity parent) =>
        [.. stopped.Where(a => a.TraceId == parent.TraceId && a.ParentSpanId == parent.SpanId)];

    /// <summary>
    /// Waits until <paramref name="count"/> spans named <paramref name="name"/> have finished, and returns them.
    /// </summary>
    public async Task<IReadOnlyList<Activity>> WaitForAsync(string name, int count = 1)
    {
        await TestHelpers.WaitUntilAsync(
            () => Named(name).Count >= count,
            $"Expected {count} finished span(s) named '{name}'.");
        return Named(name);
    }

    public void Dispose() => listener.Dispose();
}
