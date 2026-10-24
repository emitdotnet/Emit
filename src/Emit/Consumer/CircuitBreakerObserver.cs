namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Metrics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Notifies the circuit breaker of each handler attempt, and holds attempts while the circuit does not allow them.
/// </summary>
public interface ICircuitBreakerNotifier
{
    /// <summary>
    /// Waits until the circuit lets an attempt run: at once while closed, after the pause while open, and one
    /// attempt at a time while half-open.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting, on shutdown.</param>
    /// <returns>The attempt, disposed once its outcome has been reported.</returns>
    ValueTask<IAsyncDisposable> BeginAttemptAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports that an attempt succeeded. In half-open state this closes the circuit.
    /// </summary>
    ValueTask ReportSuccessAsync();

    /// <summary>
    /// Reports that an attempt failed. Filtered by <c>TripOn</c> exception types before counting toward the
    /// threshold.
    /// </summary>
    ValueTask ReportFailureAsync(Exception exception);
}

/// <summary>
/// Observes handler attempts and trips a circuit breaker when the failed attempts exceed the configured threshold
/// within a sliding window. When open, pauses the consumer group via <see cref="IConsumerFlowControl"/> and holds
/// every attempt. After the pause duration, transitions to half-open and lets one attempt at a time through as the
/// probe.
/// </summary>
/// <typeparam name="TMessage">The message type (used only for generic wiring).</typeparam>
public sealed class CircuitBreakerObserver<TMessage> : ICircuitBreakerNotifier, IDisposable
{
    private const int StateClosed = 0;
    private const int StateOpen = 1;
    private const int StateHalfOpen = 2;
    private const int MaxResumeRetries = 3;

    private static readonly TimeSpan ResumeRetryDelay = TimeSpan.FromSeconds(1);

    private readonly CircuitBreakerConfig config;
    private readonly string name;
    private readonly string consumerGroup;
    private readonly string topic;
    private readonly IConsumerFlowControl flowControl;
    private readonly EmitMetrics emitMetrics;
    private readonly ILogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Queue<long> failureTimestamps = new();

    private int state; // 0=closed, 1=open, 2=half_open
    private bool probeRunning;
    private TaskCompletionSource stateChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long circuitOpenedTicks;
    private CancellationTokenSource? halfOpenCts;
    private bool disposed;

    /// <summary>
    /// Creates a new circuit breaker observer for one consumer group declaration, and reports its state until it is
    /// disposed.
    /// </summary>
    /// <param name="config">The circuit breaker settings.</param>
    /// <param name="name">The name of the consumer group declaration, unique among the declarations.</param>
    /// <param name="consumerGroup">The consumer group the declaration consumes with, as reported in metrics.</param>
    /// <param name="topic">The topic the declaration consumes, as reported in metrics.</param>
    /// <param name="flowControl">Pauses and resumes the consumer group declaration.</param>
    /// <param name="emitMetrics">The metrics the circuit breaker reports to.</param>
    /// <param name="logger">The logger.</param>
    public CircuitBreakerObserver(
        CircuitBreakerConfig config,
        string name,
        string consumerGroup,
        string topic,
        IConsumerFlowControl flowControl,
        EmitMetrics emitMetrics,
        ILogger logger)
    {
        this.config = config;
        this.name = name;
        this.consumerGroup = consumerGroup;
        this.topic = topic;
        this.flowControl = flowControl;
        this.emitMetrics = emitMetrics;
        this.logger = logger;

        emitMetrics.RegisterCircuitBreaker(name, consumerGroup, topic, () => Volatile.Read(ref state));
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> BeginAttemptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // The observer is disposed once the consumer stopped: a worker still running then is shutting down
            cancellationToken.ThrowIfCancellationRequested();

            Task changed;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (state == StateClosed)
                {
                    return NoAttempt.Instance;
                }

                if (state == StateHalfOpen && !probeRunning)
                {
                    probeRunning = true;
                    return new ProbeAttempt(this);
                }

                changed = stateChanged.Task;
            }
            finally
            {
                gate.Release();
            }

            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReportSuccessAsync()
    {
        if (!await TryEnterGateAsync().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (state == StateHalfOpen)
            {
                state = StateClosed;
                failureTimestamps.Clear();
                emitMetrics.RecordStateTransition(consumerGroup, topic, EmitMetrics.CircuitStateClosed);
                logger.LogInformation("Circuit breaker closed — consumer group fully resumed");
                SignalStateChanged();
            }
        }
        finally
        {
            ExitGate();
        }
    }

    /// <inheritdoc />
    public async ValueTask ReportFailureAsync(Exception exception)
    {
        if (config.TripOnExceptionTypes is not null && !MatchesTripOn(exception, config.TripOnExceptionTypes))
        {
            return;
        }

        if (!await TryEnterGateAsync().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            switch (state)
            {
                case StateOpen:
                    // Already open — ignore additional failures.
                    return;

                case StateHalfOpen:
                    // Probe failed — re-open the circuit.
                    await OpenCircuitAsync().ConfigureAwait(false);
                    return;

                default:
                    // Closed — track failure in sliding window.
                    failureTimestamps.Enqueue(Stopwatch.GetTimestamp());
                    TrimWindow();

                    if (failureTimestamps.Count >= config.FailureThreshold)
                    {
                        await OpenCircuitAsync().ConfigureAwait(false);
                    }

                    return;
            }
        }
        finally
        {
            ExitGate();
        }
    }

    private async ValueTask OpenCircuitAsync()
    {
        state = StateOpen;
        circuitOpenedTicks = Stopwatch.GetTimestamp();
        failureTimestamps.Clear();
        SignalStateChanged();

        // Cancel any pending half-open transition before scheduling a new one.
        CancelHalfOpenTransition();

        // Schedule the half-open transition BEFORE calling PauseAsync
        // so that even if PauseAsync fails, the timer still fires.
        var cts = new CancellationTokenSource();
        halfOpenCts = cts;
        _ = ScheduleHalfOpenAsync(cts.Token);

        emitMetrics.RecordStateTransition(consumerGroup, topic, EmitMetrics.CircuitStateOpen);
        logger.LogWarning(
            "Circuit breaker opened — pausing consumer group. Pause duration: {PauseDuration}",
            config.PauseDuration);

        try
        {
            await flowControl.PauseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pause consumer group after circuit breaker opened");
        }
    }

    private async Task ScheduleHalfOpenAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(config.PauseDuration, ct).ConfigureAwait(false);

            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (state != StateOpen)
                {
                    return;
                }

                state = StateHalfOpen;
                SignalStateChanged();

                var openedTicks = circuitOpenedTicks;
                if (openedTicks > 0)
                {
                    emitMetrics.RecordCircuitBreakerOpenDuration(
                        consumerGroup, topic, Stopwatch.GetElapsedTime(openedTicks).TotalSeconds);
                }

                emitMetrics.RecordStateTransition(consumerGroup, topic, EmitMetrics.CircuitStateHalfOpen);
                logger.LogInformation("Circuit breaker half-open — resuming consumer group for probe message");

                await ResumeWithRetryAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during re-open or dispose.
        }
        catch (Exception ex)
        {
            // Defense in depth — log but don't crash. Could happen during shutdown
            // if the consumer is disposed between the cancellation check and the Resume call.
            logger.LogError(ex, "Unexpected error in circuit breaker half-open transition");
        }
    }

    private async Task ResumeWithRetryAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxResumeRetries; attempt++)
        {
            try
            {
                await flowControl.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to resume consumer group on half-open transition, attempt {Attempt}/{MaxAttempts}",
                    attempt, MaxResumeRetries);

                if (attempt < MaxResumeRetries)
                {
                    await Task.Delay(ResumeRetryDelay, ct).ConfigureAwait(false);
                }
            }
        }

        // All retries failed — force circuit to closed to prevent permanent pause.
        state = StateClosed;
        failureTimestamps.Clear();
        SignalStateChanged();
        emitMetrics.RecordStateTransition(consumerGroup, topic, EmitMetrics.CircuitStateClosed);
        logger.LogCritical(
            "All resume attempts failed — forcing circuit breaker to closed state to prevent permanent pause");
    }

    /// <summary>
    /// Wakes the attempts waiting for the circuit, so each checks again whether it may run. Must be called under
    /// <see cref="gate"/>.
    /// </summary>
    private void SignalStateChanged()
    {
        var changed = stateChanged;
        stateChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }

    /// <summary>
    /// Ends the half-open probe, so another attempt can be the probe while the circuit stays half-open.
    /// </summary>
    private async ValueTask EndProbeAsync()
    {
        if (!await TryEnterGateAsync().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            probeRunning = false;
            SignalStateChanged();
        }
        finally
        {
            ExitGate();
        }
    }

    /// <summary>
    /// Enters <see cref="gate"/> to report what a worker did, unless the observer was disposed: the consumer
    /// stopped, and a worker that outlived the stop has nothing left to change.
    /// </summary>
    private async ValueTask<bool> TryEnterGateAsync()
    {
        if (disposed)
        {
            return false;
        }

        try
        {
            await gate.WaitAsync().ConfigureAwait(false);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void ExitGate()
    {
        try
        {
            gate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while reporting: the consumer stopped meanwhile
        }
    }

    private void TrimWindow()
    {
        var cutoff = Stopwatch.GetTimestamp()
            - (long)(config.SamplingWindow.TotalSeconds * Stopwatch.Frequency);

        while (failureTimestamps.Count > 0 && failureTimestamps.Peek() < cutoff)
        {
            failureTimestamps.Dequeue();
        }
    }

    private void CancelHalfOpenTransition()
    {
        if (halfOpenCts is not null)
        {
            halfOpenCts.Cancel();
            halfOpenCts.Dispose();
            halfOpenCts = null;
        }
    }

    private static bool MatchesTripOn(Exception exception, Type[] tripOnTypes)
    {
        var exType = exception.GetType();
        foreach (var tripOnType in tripOnTypes)
        {
            if (tripOnType.IsAssignableFrom(exType))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        emitMetrics.DeregisterCircuitBreaker(name);
        CancelHalfOpenTransition();
        gate.Dispose();
    }

    /// <summary>
    /// An attempt that holds nothing once it ends.
    /// </summary>
    private sealed class NoAttempt : IAsyncDisposable
    {
        public static readonly NoAttempt Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// The one attempt let through while the circuit is half-open.
    /// </summary>
    private sealed class ProbeAttempt(CircuitBreakerObserver<TMessage> owner) : IAsyncDisposable
    {
        private int ended;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref ended, 1) == 0 ? owner.EndProbeAsync() : ValueTask.CompletedTask;
    }
}
