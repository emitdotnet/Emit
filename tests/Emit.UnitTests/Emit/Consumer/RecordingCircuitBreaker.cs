namespace Emit.UnitTests.Consumer;

using global::Emit.Consumer;

/// <summary>
/// Circuit breaker that lets every attempt run and records, in order, each attempt it lets in ("begin"), each
/// outcome reported ("success", "failure"), and each attempt released ("end").
/// </summary>
internal sealed class RecordingCircuitBreaker(List<string> events) : ICircuitBreakerNotifier
{
    public ValueTask<IAsyncDisposable> BeginAttemptAsync(CancellationToken cancellationToken)
    {
        Record("begin");
        return ValueTask.FromResult<IAsyncDisposable>(new Attempt(this));
    }

    public ValueTask ReportSuccessAsync()
    {
        Record("success");
        return ValueTask.CompletedTask;
    }

    public ValueTask ReportFailureAsync(Exception exception)
    {
        Record("failure");
        return ValueTask.CompletedTask;
    }

    private void Record(string @event)
    {
        lock (events)
        {
            events.Add(@event);
        }
    }

    private sealed class Attempt(RecordingCircuitBreaker owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Record("end");
            return ValueTask.CompletedTask;
        }
    }
}
