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

        // Trip the circuit
        await observer.ReportFailureAsync(new Exception("test"));
        await observer.ReportFailureAsync(new Exception("test"));

        mockFlowControl.Verify(f => f.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Wait for half-open transition
        await Task.Delay(800);

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

        // Trip the circuit
        await observer.ReportFailureAsync(new Exception("test"));
        await observer.ReportFailureAsync(new Exception("test"));

        // Wait for half-open
        await Task.Delay(800);

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
}
