namespace Emit.Observability;

using Microsoft.Extensions.Logging;

/// <summary>
/// Runs observer notifications one after another on a task scheduler, away from the code that raises them. An
/// observer that is slow or never returns delays later notifications, never leader election or daemon coordination.
/// </summary>
internal sealed class ObserverNotificationQueue(TaskScheduler scheduler, ILogger logger)
{
    /// <summary>
    /// Notifications waiting behind a slow observer beyond this many are dropped, so one that never returns
    /// cannot build up an unbounded backlog.
    /// </summary>
    internal const int MaxPending = 1024;

    private readonly Queue<Func<Task>> pending = new();
    private bool draining;

    // Completed when the queue next runs empty, for a flush waiting on it
    private TaskCompletionSource? drained;

    /// <summary>
    /// Queues <paramref name="notify"/> after the notifications queued before it.
    /// </summary>
    public void Post(Func<Task> notify)
    {
        lock (pending)
        {
            if (pending.Count >= MaxPending)
            {
                logger.LogWarning("An observer notification was dropped: {MaxPending} notifications are waiting for a slow observer", MaxPending);
                return;
            }

            pending.Enqueue(notify);
            if (draining)
            {
                return;
            }

            draining = true;
        }

        _ = Task.Factory.StartNew(
            static state => ((ObserverNotificationQueue)state!).DrainAsync(),
            this,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            scheduler).Unwrap();
    }

    /// <summary>
    /// Returns a task that completes once every queued notification has run, or after <paramref name="bound"/>,
    /// whichever comes first. A notification posted meanwhile is waited for too, within the same bound.
    /// </summary>
    public Task FlushAsync(TimeSpan bound, TimeProvider timeProvider)
    {
        Task emptied;
        lock (pending)
        {
            if (!draining)
            {
                return Task.CompletedTask;
            }

            // Completed under the lock, so its continuations must not run there
            drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            emptied = drained.Task;
        }

        return WaitAsync(emptied, bound, timeProvider);

        static async Task WaitAsync(Task emptied, TimeSpan bound, TimeProvider timeProvider)
        {
            try
            {
                await emptied.WaitAsync(bound, timeProvider).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A slow observer is not waited for any longer
            }
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Func<Task> notify;
            lock (pending)
            {
                if (!pending.TryDequeue(out notify!))
                {
                    draining = false;
                    drained?.TrySetResult();
                    drained = null;
                    return;
                }
            }

            try
            {
                await notify().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "An observer notification failed");
            }
        }
    }
}
