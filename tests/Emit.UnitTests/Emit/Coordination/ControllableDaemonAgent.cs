namespace Emit.UnitTests.Coordination;

using Emit.Abstractions.Daemon;

/// <summary>
/// Daemon whose runs are fully controlled by the test. A run ends when its token is cancelled,
/// unless <see cref="IgnoresCancellation"/> is set; it can also be made to crash or to fail at start.
/// Every transition happens synchronously, so tests stay deterministic.
/// </summary>
internal sealed class ControllableDaemonAgent(string daemonId) : IDaemonAgent
{
    private readonly object gate = new();
    private TaskCompletionSource? currentRun;
    private CancellationToken currentToken;
    private int activeRuns;
    private bool processDead;

    public string DaemonId { get; } = daemonId;

    /// <summary>
    /// Gets the number of times <see cref="RunAsync"/> was called.
    /// </summary>
    public int RunCount { get; private set; }

    /// <summary>
    /// Gets the highest number of runs that were active at the same time.
    /// </summary>
    public int MaxConcurrentRuns { get; private set; }

    /// <summary>
    /// Gets the token passed to the last run.
    /// </summary>
    public CancellationToken LastToken => LastContext?.AssignmentToken ?? default;

    /// <summary>
    /// Gets the context passed to the last run.
    /// </summary>
    public DaemonRunContext? LastContext { get; private set; }

    /// <summary>
    /// Gets or sets the number of upcoming runs that fail immediately.
    /// </summary>
    public int FailNextRuns { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether runs keep going after their token is cancelled, read when a run is
    /// cancelled.
    /// </summary>
    public bool IgnoresCancellation { get; set; }

    /// <summary>
    /// Gets a value indicating whether a run is active and the process it runs in is alive.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return activeRuns > 0 && !processDead;
            }
        }
    }

    public Task RunAsync(DaemonRunContext context)
    {
        var assignmentToken = context.AssignmentToken;
        TaskCompletionSource run;
        lock (gate)
        {
            RunCount++;
            LastContext = context;

            if (FailNextRuns > 0)
            {
                FailNextRuns--;
                return Task.FromException(new InvalidOperationException($"Daemon '{DaemonId}' failed to start."));
            }

            activeRuns++;
            MaxConcurrentRuns = Math.Max(MaxConcurrentRuns, activeRuns);
            run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            currentRun = run;
            currentToken = assignmentToken;
        }

        // Whether the run ignores its cancellation is decided when it is cancelled
        assignmentToken.Register(() =>
        {
            if (!IgnoresCancellation)
            {
                End(run, exception: null);
            }
        });

        return run.Task;
    }

    /// <summary>
    /// Ends the current run with an exception, as a daemon whose work fails does.
    /// </summary>
    public void Crash()
    {
        TaskCompletionSource? run;
        lock (gate)
        {
            run = currentRun;
        }

        if (run is not null)
        {
            End(run, new InvalidOperationException($"Daemon '{DaemonId}' crashed."));
        }
    }

    /// <summary>
    /// Ends the current run if it was cancelled, as a daemon slow to stop eventually does.
    /// </summary>
    public void FinishStop()
    {
        TaskCompletionSource? run;
        lock (gate)
        {
            run = currentToken.IsCancellationRequested ? currentRun : null;
        }

        if (run is not null)
        {
            End(run, exception: null);
        }
    }

    /// <summary>
    /// Ends the current run normally even though its token was not cancelled.
    /// </summary>
    public void Return()
    {
        TaskCompletionSource? run;
        lock (gate)
        {
            run = currentRun;
        }

        if (run is not null)
        {
            End(run, exception: null);
        }
    }

    /// <summary>
    /// Simulates the death of the process: the daemon stops running with it.
    /// </summary>
    public void ProcessDied()
    {
        lock (gate)
        {
            processDead = true;
        }
    }

    private void End(TaskCompletionSource run, Exception? exception)
    {
        // Continuations run asynchronously, so completing the run inside the lock cannot re-enter it
        lock (gate)
        {
            var ended = exception is null ? run.TrySetResult() : run.TrySetException(exception);
            if (ended)
            {
                activeRuns--;
            }
        }
    }
}
