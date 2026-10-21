namespace Emit.UnitTests.Coordination;

using System.Collections.Concurrent;

/// <summary>
/// Holds the tasks scheduled on it until <see cref="RunQueued"/> is called, as a busy thread pool does.
/// </summary>
internal sealed class QueuedTaskScheduler : TaskScheduler
{
    private readonly ConcurrentQueue<Task> queued = new();

    public void RunQueued()
    {
        while (queued.TryDequeue(out var task))
        {
            TryExecuteTask(task);
        }
    }

    protected override void QueueTask(Task task) => queued.Enqueue(task);

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

    protected override IEnumerable<Task> GetScheduledTasks() => queued;
}
