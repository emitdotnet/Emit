namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using global::Emit.Tracing;
using global::Emit.UnitTests.Consumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Runs the pipeline the composer builds for a consumer with a circuit breaker, and checks that the breaker is
/// told about each handler attempt exactly once, when it ends, and that each attempt goes through the breaker.
/// </summary>
public sealed class ConsumerPipelineCircuitBreakerTests
{
    private readonly List<string> events = [];

    [Fact]
    public async Task GivenNoRetryPolicy_WhenTheHandlerFails_ThenOneFailureIsReportedBeforeTheErrorAction()
    {
        // Arrange
        var pipeline = Compose(retryPolicy: null, handler: Fail);

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(["begin", "attempt", "failure", "end", "dead-letter"], events);
    }

    [Fact]
    public async Task GivenNoRetryPolicy_WhenTheHandlerSucceeds_ThenOneSuccessIsReported()
    {
        // Arrange
        var pipeline = Compose(retryPolicy: null, handler: _ => Task.CompletedTask);

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(["begin", "attempt", "success", "end"], events);
    }

    [Fact]
    public async Task GivenThreeRetries_WhenEveryAttemptFails_ThenEachOfTheFourAttemptsReportsOneFailureBeforeTheNext()
    {
        // Arrange: three retries make four attempts
        var pipeline = Compose(retryPolicy: RetryAll(3), handler: Fail);

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(
            [.. Enumerable.Repeat<string[]>(["begin", "attempt", "failure", "end"], 4).SelectMany(e => e), "dead-letter"],
            events);
    }

    [Fact]
    public async Task GivenThreeRetries_WhenTheThirdAttemptSucceeds_ThenTwoFailuresThenOneSuccessAreReported()
    {
        // Arrange
        var attempts = 0;
        var pipeline = Compose(retryPolicy: RetryAll(3), handler: context => ++attempts < 3 ? Fail(context) : Task.CompletedTask);

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(
            ["begin", "attempt", "failure", "end", "begin", "attempt", "failure", "end", "begin", "attempt", "success", "end"],
            events);
    }

    [Fact]
    public async Task GivenPolicyThatDoesNotRetryTheException_WhenTheHandlerFails_ThenOneFailureIsReported()
    {
        // Arrange: only InvalidOperationException is retried
        var pipeline = Compose(retryPolicy: RetryOnly<InvalidOperationException>(3), handler: _ => throw new ArgumentException("not retried"));

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(["begin", "attempt", "failure", "end", "dead-letter"], events);
    }

    [Fact]
    public async Task GivenRetryableFailureThenANonRetryableOne_WhenTheMessageIsHandled_ThenEachAttemptReportsOneFailure()
    {
        // Arrange
        var attempts = 0;
        var pipeline = Compose(
            retryPolicy: RetryOnly<InvalidOperationException>(3),
            handler: _ => ++attempts == 1 ? throw new InvalidOperationException("retried") : throw new ArgumentException("not retried"));

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(["begin", "attempt", "failure", "end", "begin", "attempt", "failure", "end", "dead-letter"], events);
    }

    [Fact]
    public async Task GivenThreeRetries_WhenTheValidatorThrowsEveryTime_ThenEachOfTheFourAttemptsReportsOneFailure()
    {
        // Arrange: a validator that throws is a failed attempt, like a handler that throws
        var pipeline = Compose(retryPolicy: RetryAll(3), handler: Fail, validation: new ThrowingMiddleware());

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(
            [.. Enumerable.Repeat<string[]>(["begin", "failure", "end"], 4).SelectMany(e => e), "dead-letter"],
            events);
    }

    [Fact]
    public async Task GivenThreeRetries_WhenTheFilterThrowsEveryTime_ThenEachOfTheFourAttemptsReportsOneFailure()
    {
        // Arrange
        var pipeline = Compose(retryPolicy: RetryAll(3), handler: Fail, filter: new ThrowingMiddleware());

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(
            [.. Enumerable.Repeat<string[]>(["begin", "failure", "end"], 4).SelectMany(e => e), "dead-letter"],
            events);
    }

    [Fact]
    public async Task GivenNoRetryPolicy_WhenTheMessageIsFilteredOut_ThenOneSuccessIsReported()
    {
        // Arrange: an attempt that does not fail is a success, wherever it ends
        var pipeline = Compose(retryPolicy: null, handler: Fail, filter: new SkippingMiddleware());

        // Act
        await RunAsync(pipeline);

        // Assert
        Assert.Equal(["begin", "success", "end"], events);
    }

    [Fact]
    public async Task GivenRetryPolicy_WhenTheAttemptIsCancelledByShutdown_ThenNothingIsReported()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var pipeline = Compose(retryPolicy: RetryAll(3), handler: _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        // Act
        var failure = await Record.ExceptionAsync(() => RunAsync(pipeline, cts.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(["begin", "attempt", "end"], events);
    }

    // ── Helpers ──

    private static Func<Exception, ErrorAction> RetryAll(int retries) =>
        _ => ErrorAction.Retry(retries, Backoff.None, ErrorAction.DeadLetter());

    private static Func<Exception, ErrorAction> RetryOnly<TException>(int retries)
        where TException : Exception =>
        exception => exception is TException
            ? ErrorAction.Retry(retries, Backoff.None, ErrorAction.DeadLetter())
            : ErrorAction.DeadLetter();

    private static Task Fail(ConsumeContext<string> context) => throw new InvalidOperationException("downstream unavailable");

    private IMiddlewarePipeline<ConsumeContext<string>> Compose(
        Func<Exception, ErrorAction>? retryPolicy,
        Func<ConsumeContext<string>, Task> handler,
        IMiddleware<ConsumeContext<string>>? validation = null,
        IMiddleware<ConsumeContext<string>>? filter = null)
    {
        var deadLetterSink = new Mock<IDeadLetterSink>();
        deadLetterSink.SetupGet(s => s.DestinationAddress).Returns(new Uri("kafka://localhost:9092/dlq"));
        deadLetterSink
            .Setup(s => s.ProduceAsync(It.IsAny<byte[]?>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<KeyValuePair<string, string>>>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("dead-letter"))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(new EmitMetrics(null, new EmitMetricsEnrichment()));
        services.AddSingleton<INodeIdentity>(new Mock<INodeIdentity>().Object);
        services.AddSingleton<IOptions<EmitTracingOptions>>(Options.Create(new EmitTracingOptions()));
        services.AddSingleton(new ActivityEnricherInvoker(NullLogger<ActivityEnricherInvoker>.Instance));
        services.AddSingleton(deadLetterSink.Object);

        var composer = new ConsumerPipelineComposer<string>
        {
            Services = services.BuildServiceProvider(),
            LoggerFactory = NullLoggerFactory.Instance,
            ConsumeObservers = [],
            GroupPipeline = new MessagePipelineBuilder(),
            GlobalInboundPipeline = new MessagePipelineBuilder(),
            ProviderInboundPipeline = new MessagePipelineBuilder(),
            ValidationMiddleware = validation,
            FilterMiddleware = filter,
            RetryPolicy = retryPolicy,
            ErrorPolicy = _ => ErrorAction.DeadLetter(),
            CircuitBreakerNotifier = new RecordingCircuitBreaker(events),
        };

        return composer.Compose(
            new TestPipeline<ConsumeContext<string>>(context =>
            {
                events.Add("attempt");
                return handler(context);
            }),
            perEntryPipeline: null,
            identifier: "test",
            kind: ConsumerKind.Direct,
            consumerType: null).Pipeline;
    }

    private static Task RunAsync(IMiddlewarePipeline<ConsumeContext<string>> pipeline, CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return pipeline.InvokeAsync(new ConsumeContext<string>
        {
            MessageId = "message-1",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken,
            Services = services,
            Message = "message",
            TransportContext = TestTransportContext.Create(services),
        });
    }

    private sealed class ThrowingMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next) =>
            throw new InvalidOperationException("dependency unavailable");
    }

    private sealed class SkippingMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next) =>
            Task.CompletedTask;
    }
}
