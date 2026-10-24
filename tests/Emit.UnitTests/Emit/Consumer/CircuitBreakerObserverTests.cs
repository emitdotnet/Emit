namespace Emit.UnitTests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Consumer;
using global::Emit.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class CircuitBreakerObserverTests
{
    private readonly Mock<IConsumerFlowControl> mockFlowControl = new();
    private readonly ILogger logger = NullLogger.Instance;

    private CircuitBreakerObserver<string> CreateObserver(
        int failureThreshold = 3,
        TimeSpan? samplingWindow = null,
        TimeSpan? pauseDuration = null,
        Type[]? tripOnExceptionTypes = null)
    {
        var config = new CircuitBreakerConfig(
            failureThreshold,
            samplingWindow ?? TimeSpan.FromSeconds(30),
            pauseDuration ?? TimeSpan.FromSeconds(5),
            tripOnExceptionTypes);

        return new CircuitBreakerObserver<string>(config, mockFlowControl.Object, new EmitMetrics(null, new EmitMetricsEnrichment()), logger);
    }

    /// <summary>
    /// Returns a task that completes when the circuit resumes the consumer, which it does as it goes half-open.
    /// </summary>
    private Task HalfOpenAsync()
    {
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockFlowControl
            .Setup(f => f.ResumeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => resumed.TrySetResult())
            .Returns(Task.CompletedTask);
        return resumed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivenTripOnMatchingException_WhenFailureThresholdReached_ThenPauseAsyncCalled()
    {
        // Arrange — threshold of 3 with TripOn<InvalidOperationException>
        using var observer = CreateObserver(
            failureThreshold: 3,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromSeconds(5),
            tripOnExceptionTypes: [typeof(InvalidOperationException)]);

        // Act — 3 matching failures should trip the breaker
        for (var i = 0; i < 3; i++)
        {
            await observer.ReportFailureAsync(new InvalidOperationException("test"));
        }

        // Assert — circuit should have opened and called PauseAsync
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenTripOnNonMatchingException_WhenFailureOccurs_ThenPauseAsyncNotCalled()
    {
        // Arrange — threshold of 2, TripOn<InvalidOperationException>, throw TimeoutException
        using var observer = CreateObserver(
            failureThreshold: 2,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromSeconds(5),
            tripOnExceptionTypes: [typeof(InvalidOperationException)]);

        // Act — throw non-matching exceptions (should NOT trip the breaker)
        for (var i = 0; i < 5; i++)
        {
            await observer.ReportFailureAsync(new TimeoutException("test"));
        }

        // Assert — circuit should NOT have opened
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenNoTripOnConfigured_WhenAnyExceptionTypeReported_ThenAllCountTowardThreshold()
    {
        // Arrange — threshold of 2, no TripOn filter (all exceptions count)
        using var observer = CreateObserver(
            failureThreshold: 2,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromSeconds(5),
            tripOnExceptionTypes: null);

        // Act — 2 failures with different exception types
        await observer.ReportFailureAsync(new InvalidOperationException("test"));
        await observer.ReportFailureAsync(new TimeoutException("test"));

        // Assert — circuit should have opened (both types counted)
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenFailureThresholdReached_WhenCircuitOpens_ThenPauseAsyncCalled()
    {
        // Arrange — threshold of 2
        using var observer = CreateObserver(
            failureThreshold: 2,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromSeconds(5));

        // Act — 2 failures to trip the breaker
        await observer.ReportFailureAsync(new Exception("test"));
        await observer.ReportFailureAsync(new Exception("test"));

        // Assert — PauseAsync should be called exactly once when circuit opens
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCircuitHalfOpen_WhenSuccessReported_ThenCircuitClosesAndResumeAsyncCalled()
    {
        // Arrange — threshold of 2, short pause to transition to half-open quickly
        using var observer = CreateObserver(
            failureThreshold: 2,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromMilliseconds(600));

        var halfOpen = HalfOpenAsync();

        // Trip the circuit
        await observer.ReportFailureAsync(new Exception("test"));
        await observer.ReportFailureAsync(new Exception("test"));

        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Wait for half-open transition
        await halfOpen;

        // Assert — ResumeAsync should be called (half-open transition resumes partitions)
        mockFlowControl.Verify(f => f.ResumeAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Act — successful message in half-open state should close the circuit
        await observer.ReportSuccessAsync();

        // Assert — no additional PauseAsync calls (circuit closed successfully)
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCircuitHalfOpen_WhenFailureReported_ThenCircuitReopensAndPausesAgain()
    {
        // Arrange — threshold of 2, short pause
        using var observer = CreateObserver(
            failureThreshold: 2,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromMilliseconds(600));

        var halfOpen = HalfOpenAsync();

        // Trip the circuit
        await observer.ReportFailureAsync(new Exception("test"));
        await observer.ReportFailureAsync(new Exception("test"));

        // Wait for half-open
        await halfOpen;

        // Act — failing message in half-open state should re-open the circuit
        await observer.ReportFailureAsync(new Exception("half-open failure"));

        // Assert — PauseAsync called again (circuit re-opened)
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GivenSuccessfulProcessing_WhenBelowThreshold_ThenCircuitRemainsClosedAndNoPause()
    {
        // Arrange — threshold of 3
        using var observer = CreateObserver(
            failureThreshold: 3,
            samplingWindow: TimeSpan.FromSeconds(30),
            pauseDuration: TimeSpan.FromSeconds(5));

        // Act — all successful messages
        for (var i = 0; i < 10; i++)
        {
            await observer.ReportSuccessAsync();
        }

        // Assert — no pause calls
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenThresholdOfThree_WhenFailuresAreReported_ThenTheCircuitOpensOnExactlyTheThird()
    {
        // Arrange
        using var observer = CreateObserver(failureThreshold: 3);

        // Act
        await observer.ReportFailureAsync(new Exception("1"));
        await observer.ReportFailureAsync(new Exception("2"));
        var pausedAfterTwo = mockFlowControl.Invocations.Any(i => i.Method.Name == nameof(IConsumerFlowControl.PauseAsync));
        await observer.ReportFailureAsync(new Exception("3"));

        // Assert
        Assert.False(pausedAfterTwo);
        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenClosedCircuit_WhenAnAttemptBegins_ThenItRunsAtOnce()
    {
        // Arrange
        using var observer = CreateObserver();

        // Act
        var attempt = observer.BeginAttemptAsync(CancellationToken.None);

        // Assert
        Assert.True(attempt.IsCompletedSuccessfully);
        await (await attempt).DisposeAsync();
    }

    [Fact]
    public async Task GivenOpenCircuit_WhenAnAttemptBegins_ThenItWaitsUntilTheCircuitGoesHalfOpen()
    {
        // Arrange
        using var observer = CreateObserver(failureThreshold: 1, pauseDuration: TimeSpan.FromMilliseconds(500));
        var halfOpen = HalfOpenAsync();
        await observer.ReportFailureAsync(new Exception("trip"));

        // Act
        var attempt = observer.BeginAttemptAsync(CancellationToken.None).AsTask();
        var ranWhileOpen = attempt.IsCompleted;
        await halfOpen;

        // Assert
        Assert.False(ranWhileOpen);
        await (await attempt.WaitAsync(TimeSpan.FromSeconds(30))).DisposeAsync();
    }

    [Fact]
    public async Task GivenHalfOpenCircuit_WhenTwoAttemptsBegin_ThenOnlyTheProbeRunsAndTheOtherRunsOnceTheProbeSucceeds()
    {
        // Arrange
        using var observer = await HalfOpenObserverAsync();

        // Act
        var probe = await observer.BeginAttemptAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var second = observer.BeginAttemptAsync(CancellationToken.None).AsTask();
        var secondRanWithProbe = second.IsCompleted;
        await observer.ReportSuccessAsync();
        await probe.DisposeAsync();

        // Assert
        Assert.False(secondRanWithProbe);
        await (await second.WaitAsync(TimeSpan.FromSeconds(30))).DisposeAsync();
    }

    [Fact]
    public async Task GivenHalfOpenCircuit_WhenTheProbeFails_ThenTheCircuitReopensAndTheOtherAttemptKeepsWaiting()
    {
        // Arrange: once it reopens, the circuit stays open for three seconds, longer than the check below takes
        using var observer = await HalfOpenObserverAsync(pause: TimeSpan.FromSeconds(3));

        // Act
        var probe = await observer.BeginAttemptAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var second = observer.BeginAttemptAsync(CancellationToken.None).AsTask();
        await observer.ReportFailureAsync(new Exception("probe failed"));
        await probe.DisposeAsync();

        // Assert
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public async Task GivenHalfOpenCircuit_WhenTheProbeFailsWithAnExceptionTheCircuitIgnores_ThenTheNextAttemptBecomesTheProbe()
    {
        // Arrange: only InvalidOperationException trips the circuit
        using var observer = await HalfOpenObserverAsync(tripOn: [typeof(InvalidOperationException)]);

        // Act
        var probe = await observer.BeginAttemptAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var second = observer.BeginAttemptAsync(CancellationToken.None).AsTask();
        await observer.ReportFailureAsync(new ArgumentException("ignored"));
        await probe.DisposeAsync();

        // Assert
        await (await second.WaitAsync(TimeSpan.FromSeconds(30))).DisposeAsync();
    }

    [Fact]
    public async Task GivenOpenCircuit_WhenShutdownCancelsAWaitingAttempt_ThenTheWaitEnds()
    {
        // Arrange
        using var observer = CreateObserver(failureThreshold: 1, pauseDuration: TimeSpan.FromMinutes(1));
        await observer.ReportFailureAsync(new Exception("trip"));
        using var shutdown = new CancellationTokenSource();
        var attempt = observer.BeginAttemptAsync(shutdown.Token).AsTask();

        // Act
        await shutdown.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task GivenStoppedConsumer_WhenAWorkerThatOutlivedTheStopReportsAndRetries_ThenOnlyTheShutdownSurfaces()
    {
        // Arrange: the observer is disposed once the consumer stopped, after the shutdown was requested
        var observer = CreateObserver(failureThreshold: 1);
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        observer.Dispose();

        // Act
        await observer.ReportFailureAsync(new Exception("late failure"));
        await observer.ReportSuccessAsync();
        var attempt = observer.BeginAttemptAsync(shutdown.Token).AsTask();

        // Assert: a shutdown is never a failure the error policy acts on
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
    }

    /// <summary>
    /// Returns an observer whose circuit opened on one failure and has gone half-open.
    /// </summary>
    private async Task<CircuitBreakerObserver<string>> HalfOpenObserverAsync(TimeSpan? pause = null, Type[]? tripOn = null)
    {
        var halfOpen = HalfOpenAsync();
        var observer = CreateObserver(
            failureThreshold: 1,
            pauseDuration: pause ?? TimeSpan.FromMilliseconds(300),
            tripOnExceptionTypes: tripOn);
        await observer.ReportFailureAsync(new InvalidOperationException("trip"));
        await halfOpen;
        return observer;
    }
}
