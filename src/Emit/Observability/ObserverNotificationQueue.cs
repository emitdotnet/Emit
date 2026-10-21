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
