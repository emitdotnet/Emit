namespace Emit.LeaderElection;

using Microsoft.Extensions.Logging;

/// <summary>
/// A cancellation source that cancels itself when a lease runs out. Callbacks registered on its token are
/// code Emit does not control, so they run on a callback scheduler instead of the thread that cancels: one
/// that blocks holds up neither the heartbeat nor the lease timer, and one that throws is logged.
/// </summary>
/// <remarks>
/// The lease is measured with <see cref="ClockReading"/>, so it also runs out while the system is suspended.
/// Timers do not advance during a suspend either, so the expiry is checked at least every check interval rather
/// than once at the expected end, and whenever <see cref="IsCancellationRequested"/> is read.
/// </remarks>
internal sealed class LeaseTokenSource : IDisposable
{
    private readonly CancellationTokenSource source = new();
    private readonly object gate = new();
    private readonly ITimer expiry;
    private readonly TimeProvider timeProvider;
    private readonly TaskScheduler callbackScheduler;
    private readonly TimeSpan checkInterval;
    private readonly ILogger logger;
    private ClockReading heldSince;
    private TimeSpan lease = Timeout.InfiniteTimeSpan;
    private int cancelled;

    public LeaseTokenSource(TimeProvider timeProvider, TaskScheduler callbackScheduler, TimeSpan checkInterval, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(callbackScheduler);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(checkInterval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        this.timeProvider = timeProvider;
        this.callbackScheduler = callbackScheduler;
        this.checkInterval = checkInterval;
        this.logger = logger;
        Token = source.Token;
        expiry = timeProvider.CreateTimer(
            static state => ((LeaseTokenSource)state!).CheckExpiry(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Gets the token cancelled when the lease runs out or <see cref="Cancel"/> is called. It stays readable
    /// after the source is disposed.
    /// </summary>
    public CancellationToken Token { get; }

    /// <summary>
    /// Gets a value indicating whether the lease ended. It is set as soon as the lease ends, before the
    /// token's callbacks run.
    /// </summary>
    public bool IsCancellationRequested
    {
        get
        {
            if (Volatile.Read(ref cancelled) == 1)
            {
                return true;
            }

            var remaining = Remaining();
            if (remaining == Timeout.InfiniteTimeSpan || remaining > TimeSpan.Zero)
            {
                return false;
            }

            Cancel();
            return true;
        }
    }

    /// <summary>
    /// Ends the lease <paramref name="lease"/> after <paramref name="heldSince"/>, replacing any earlier expiry.
    /// A lease with nothing left is cancelled at once.
    /// </summary>
    public void ExpireAt(ClockReading heldSince, TimeSpan lease)
    {
        lock (gate)
        {
            this.heldSince = heldSince;
            this.lease = lease;
        }

        CheckExpiry();
    }

    /// <summary>
    /// Ends the lease now and cancels the token on the callback scheduler.
    /// </summary>
    public void Cancel()
    {
        if (Interlocked.Exchange(ref cancelled, 1) == 1)
        {
            return;
        }

        _ = Task.Factory.StartNew(
            static state => ((LeaseTokenSource)state!).CancelToken(),
            this,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            callbackScheduler);
    }

    /// <summary>
    /// Stops the expiry timer. The token source itself holds no timer or link, so it needs no disposal, and
    /// leaving it lets callbacks still running on it finish and the token be read afterwards.
    /// </summary>
    public void Dispose() => expiry.Dispose();

    private TimeSpan Remaining()
    {
        lock (gate)
        {
            return lease == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : lease - heldSince.Elapsed(timeProvider);
        }
    }

    private void CheckExpiry()
    {
        var remaining = Remaining();
        if (remaining == Timeout.InfiniteTimeSpan || Volatile.Read(ref cancelled) == 1)
        {
            return;
        }

        if (remaining <= TimeSpan.Zero)
        {
            Cancel();
            return;
        }

        try
        {
            expiry.Change(remaining < checkInterval ? remaining : checkInterval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // The lease was replaced and disposed while its expiry was being checked
        }
    }

    private void CancelToken()
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException ex)
        {
            logger.LogWarning(ex, "A callback registered on a lease token failed");
        }
    }
}
