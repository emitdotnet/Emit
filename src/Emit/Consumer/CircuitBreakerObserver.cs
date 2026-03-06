namespace Emit.Consumer;

using System.Diagnostics;
using Emit.Abstractions;
using Emit.Metrics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Notifies the circuit breaker of message processing outcomes. Called by
/// <see cref="ErrorHandlingMiddleware{TMessage}"/> after each message is fully handled.
/// </summary>
public interface ICircuitBreakerNotifier
{
    /// <summary>
    /// Reports that a message was processed successfully (first attempt or retry).
    /// In half-open state this closes the circuit.
    /// </summary>
    ValueTask ReportSuccessAsync();

    /// <summary>
    /// Reports that a message processing failed terminally (discarded or dead-lettered).
    /// Filtered by <c>TripOn</c> exception types before counting toward the threshold.
    /// </summary>
    ValueTask ReportFailureAsync(Exception exception);
}

/// <summary>
/// Observes message processing outcomes and trips a circuit breaker when the failure
/// rate exceeds the configured threshold within a sliding window. When open, pauses
/// the consumer group via <see cref="IConsumerFlowControl"/>. After the pause duration,
/// transitions to half-open and resumes for a probe message.
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
    private readonly IConsumerFlowControl flowControl;
    private readonly EmitMetrics emitMetrics;
    private readonly ILogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Queue<long> failureTimestamps = new();

    private int state; // 0=closed, 1=open, 2=half_open
    private long circuitOpenedTicks;
    private CancellationTokenSource? halfOpenCts;
    private bool disposed;

    /// <summary>
    /// Creates a new circuit breaker observer.
    /// </summary>
    public CircuitBreakerObserver(
        CircuitBreakerConfig config,
        IConsumerFlowControl flowControl,
        EmitMetrics emitMetrics,
        ILogger logger)
    {
        this.config = config;
        this.flowControl = flowControl;
        this.emitMetrics = emitMetrics;
        this.logger = logger;

        emitMetrics.RegisterCircuitBreakerStateCallback(() => Volatile.Read(ref state));
    }

    /// <inheritdoc />
    public async ValueTask ReportSuccessAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (state == StateHalfOpen)
            {
                state = StateClosed;
                failureTimestamps.Clear();
                emitMetrics.RecordStateTransition("closed");
                logger.LogInformation("Circuit breaker closed — consumer group fully resumed");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask ReportFailureAsync(Exception exception)
    {
        if (config.TripOnExceptionTypes is not null && !MatchesTripOn(exception, config.TripOnExceptionTypes))
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
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
            gate.Release();
        }
    }

    private async ValueTask OpenCircuitAsync()
    {
        state = StateOpen;
        circuitOpenedTicks = Stopwatch.GetTimestamp();
        failureTimestamps.Clear();

        // Cancel any pending half-open transition before scheduling a new one.
        CancelHalfOpenTransition();

        // Schedule the half-open transition BEFORE calling PauseAsync
        // so that even if PauseAsync fails, the timer still fires.
        var cts = new CancellationTokenSource();
        halfOpenCts = cts;
        _ = ScheduleHalfOpenAsync(cts.Token);

        emitMetrics.RecordStateTransition("open");
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

                var openedTicks = circuitOpenedTicks;
                if (openedTicks > 0)
                {
                    emitMetrics.RecordCircuitBreakerOpenDuration(
                        Stopwatch.GetElapsedTime(openedTicks).TotalSeconds);
                }

                emitMetrics.RecordStateTransition("half_open");
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
        emitMetrics.RecordStateTransition("closed");
        logger.LogCritical(
            "All resume attempts failed — forcing circuit breaker to closed state to prevent permanent pause");
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
        CancelHalfOpenTransition();
        gate.Dispose();
    }
}
