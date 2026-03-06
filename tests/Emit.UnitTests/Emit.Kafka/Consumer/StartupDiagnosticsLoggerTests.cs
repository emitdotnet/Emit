namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Kafka.Consumer;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class StartupDiagnosticsLoggerTests
{
    // ── Test infrastructure ──

    private sealed class TestConsumerA : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestConsumerB : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static ConsumerGroupRegistration<string, string> CreateRegistration(
        ErrorPolicy? groupErrorPolicy = null,
        ErrorAction? deserializationErrorAction = null,
        DeadLetterTopicMap? deadLetterTopicMap = null,
        bool circuitBreakerEnabled = false,
        bool rateLimitEnabled = false,
        string topicName = "orders",
        IReadOnlyList<Type>? consumerTypes = null)
    {
        return new ConsumerGroupRegistration<string, string>
        {
            TopicName = topicName,
            GroupId = "group-1",
            BuildConsumerPipelines = () => [],
            WorkerCount = 3,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            GroupErrorPolicy = groupErrorPolicy,
            DeserializationErrorAction = deserializationErrorAction,
            DeadLetterTopicMap = deadLetterTopicMap ?? DeadLetterTopicMap.Empty,
            ConsumerTypes = consumerTypes ?? [typeof(TestConsumerA)],
            CircuitBreakerEnabled = circuitBreakerEnabled,
            RateLimitEnabled = rateLimitEnabled,
        };
    }

    // ── 1. Configured group logs group-level fields ──

    [Fact]
    public void GivenConfiguredGroup_WhenLog_ThenLogsGroupLevelFields()
    {
        // Arrange
        var logger = new CapturingLogger();
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy.Default(a => a.Discard());

        var registration = CreateRegistration(
            groupErrorPolicy: groupPolicy.Build(),
            deserializationErrorAction: ErrorAction.DeadLetter("orders.dlt"),
            circuitBreakerEnabled: true,
            rateLimitEnabled: true,
            consumerTypes: [typeof(TestConsumerA)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert — group-level Information log
        var groupLog = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("Consumer group group-1 on topic orders"));
        Assert.Contains("Workers=3", groupLog.Message);
        Assert.Contains("Distribution=ByKeyHash", groupLog.Message);
        Assert.Contains("Buffer=32", groupLog.Message);
        Assert.Contains("CircuitBreaker=Enabled", groupLog.Message);
        Assert.Contains("RateLimit=Enabled", groupLog.Message);
        Assert.Contains("DeserializationError=DeadLetter(orders.dlt)", groupLog.Message);
    }

    // ── 2. Configured consumer logs per-consumer fields ──

    [Fact]
    public void GivenConfiguredConsumer_WhenLog_ThenLogsPerConsumerFields()
    {
        // Arrange
        var logger = new CapturingLogger();
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy
            .When<TimeoutException>(a => a.Retry(3, Backoff.None).Discard())
            .Default(a => a.DeadLetter());

        var dlqEntries = new[]
        {
            new DeadLetterEntry
            {
                ConsumerKey = nameof(TestConsumerA),
                SourceTopic = "orders",
                DeadLetterTopic = "orders.dlt",
            },
        };

        var registration = CreateRegistration(
            groupErrorPolicy: groupPolicy.Build(),
            deadLetterTopicMap: new DeadLetterTopicMap(dlqEntries),
            circuitBreakerEnabled: true,
            consumerTypes: [typeof(TestConsumerA)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert — per-consumer Information log
        var consumerLog = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains(nameof(TestConsumerA)));
        Assert.Contains("ErrorPolicy=Group", consumerLog.Message);
        Assert.Contains("DLQ=orders.dlt", consumerLog.Message);
        Assert.Contains("When<TimeoutException>: Retry(3x, then Discard)", consumerLog.Message);
        Assert.Contains("Default: DeadLetter", consumerLog.Message);
    }

    // ── 3. No OnError logs Warning ──

    [Fact]
    public void GivenNoOnError_WhenLog_ThenLogsWarning()
    {
        // Arrange
        var logger = new CapturingLogger();
        var registration = CreateRegistration(consumerTypes: [typeof(TestConsumerA)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert
        var warning = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains(nameof(TestConsumerA))
            && e.Message.Contains("No error handling configured"));
        Assert.Contains("group-1", warning.Message);
        Assert.Contains("discarded without retry", warning.Message);
    }

    // ── 4. Consumer on DLQ topic logs Warning ──

    [Fact]
    public void GivenConsumerOnDlqTopic_WhenLog_ThenLogsWarning()
    {
        // Arrange — consuming from "orders.dlt" and DLQ resolves back to "orders.dlt"
        var logger = new CapturingLogger();
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy.Default(a => a.DeadLetter());

        var dlqEntries = new[]
        {
            new DeadLetterEntry
            {
                ConsumerKey = null,
                SourceTopic = "orders.dlt",
                DeadLetterTopic = "orders.dlt",
            },
        };

        var registration = CreateRegistration(
            groupErrorPolicy: groupPolicy.Build(),
            topicName: "orders.dlt",
            deadLetterTopicMap: new DeadLetterTopicMap(dlqEntries),
            circuitBreakerEnabled: true,
            consumerTypes: [typeof(TestConsumerA)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert
        var warning = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("infinite loop"));
        Assert.Contains(nameof(TestConsumerA), warning.Message);
        Assert.Contains("orders.dlt", warning.Message);
    }

    // ── 5. Worst-case retry exceeds max.poll.interval.ms logs Warning ──

    [Fact]
    public void GivenWorstCaseRetryExceedsMaxPollInterval_WhenLog_ThenLogsWarning()
    {
        // Arrange — Retry(100x, Fixed(5s)) = 500s > 300s max.poll.interval.ms
        var logger = new CapturingLogger();
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy.Default(a => a.Retry(100, Backoff.Fixed(TimeSpan.FromSeconds(5))).Discard());

        var registration = CreateRegistration(
            groupErrorPolicy: groupPolicy.Build(),
            circuitBreakerEnabled: true,
            consumerTypes: [typeof(TestConsumerA)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert
        var warning = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("max.poll.interval.ms"));
        Assert.Contains(nameof(TestConsumerA), warning.Message);
        Assert.Contains("kicked from group", warning.Message);
    }

    // ── 6. Group-level OnError shows "Group" for all consumers ──

    [Fact]
    public void GivenGroupOnError_WhenLog_ThenShowsGroupForAllConsumers()
    {
        // Arrange — group-level policy applies to all consumers
        var logger = new CapturingLogger();
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy.Default(a => a.DeadLetter());

        var registration = CreateRegistration(
            groupErrorPolicy: groupPolicy.Build(),
            circuitBreakerEnabled: true,
            consumerTypes: [typeof(TestConsumerA), typeof(TestConsumerB)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert — both consumers show "Group"
        var consumerALog = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Information
            && e.Message.Contains(nameof(TestConsumerA))
            && e.Message.Contains("ErrorPolicy=Group"));

        var consumerBLog = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Information
            && e.Message.Contains(nameof(TestConsumerB))
            && e.Message.Contains("ErrorPolicy=Group"));
    }

    // ── 7. Retries without circuit breaker logs Warning ──

    [Fact]
    public void GivenRetriesWithoutCircuitBreaker_WhenLog_ThenLogsWarning()
    {
        // Arrange
        var logger = new CapturingLogger();
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy.Default(a => a.Retry(3, Backoff.None).Discard());

        var registration = CreateRegistration(
            groupErrorPolicy: groupPolicy.Build(),
            circuitBreakerEnabled: false,
            consumerTypes: [typeof(TestConsumerA)]);

        // Act
        StartupDiagnosticsLogger.Log(registration, logger);

        // Assert
        var warning = Assert.Single(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("retries but no circuit breaker"));
        Assert.Contains(nameof(TestConsumerA), warning.Message);
    }
}
