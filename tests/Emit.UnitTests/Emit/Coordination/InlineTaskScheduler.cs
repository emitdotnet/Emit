namespace Emit.UnitTests.Coordination;

/// <summary>
/// Runs every task synchronously on the thread that schedules it, so daemon runs start before the
/// heartbeat that started them returns and multi-node tests stay deterministic.
/// </summary>
internal sealed class InlineTaskScheduler : TaskScheduler
{
    public static InlineTaskScheduler Instance { get; } = new();

    protected override void QueueTask(Task task) => TryExecuteTask(task);

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() => [];
}
