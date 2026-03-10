namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.DependencyInjection;
using global::Emit.Kafka;
using global::Emit.Kafka.Consumer;
using global::Emit.Kafka.DependencyInjection;
using Xunit;

public sealed class RegistrationValidationTests
{
    private sealed class TestConsumerA : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestConsumerB : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // ── OnError without Default throws ──

    [Fact]
    public void GivenOnErrorWithoutDefault_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var policyBuilder = new ErrorPolicyBuilder();
        policyBuilder.When<InvalidOperationException>(a => a.Retry(3, Backoff.None).Discard());
        // No Default() call

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => policyBuilder.Build());
        Assert.Contains("Default error action is required", ex.Message);
    }

    // ── OnError with Default succeeds ──

    [Fact]
    public void GivenOnErrorWithDefault_WhenBuild_ThenSucceeds()
    {
        // Arrange
        var policyBuilder = new ErrorPolicyBuilder();
        policyBuilder
            .When<InvalidOperationException>(a => a.Retry(3, Backoff.None).Discard())
            .Default(a => a.Discard());

        // Act
        var policy = policyBuilder.Build();

        // Assert
        Assert.NotNull(policy);
    }

    // ── Group-level error policy ──

    [Fact]
    public void GivenGroupLevelOnly_WhenCheckingPolicy_ThenGroupLevelUsed()
    {
        // Arrange
        var groupPolicy = new ErrorPolicyBuilder();
        groupPolicy.Default(a => a.DeadLetter());

        var registration = new ConsumerGroupRegistration<string, string>
        {
            TopicName = "orders",
            GroupId = "group-1",
            DestinationAddress = new Uri("kafka://broker:9092/kafka/test-topic"),
            BuildConsumerPipelines = () => [],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            GroupErrorPolicy = groupPolicy.Build(),
        };

        // Assert
        Assert.NotNull(registration.GroupErrorPolicy);
        Assert.IsType<ErrorAction.DeadLetterAction>(registration.GroupErrorPolicy.Evaluate(new InvalidOperationException()));
    }

    // ── No error handling ──

    [Fact]
    public void GivenNoErrorHandling_WhenCheckingPolicy_ThenGroupErrorPolicyIsNull()
    {
        // Arrange
        var registration = new ConsumerGroupRegistration<string, string>
        {
            TopicName = "orders",
            GroupId = "group-1",
            DestinationAddress = new Uri("kafka://broker:9092/kafka/test-topic"),
            BuildConsumerPipelines = () => [],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
        };

        // Assert
        Assert.Null(registration.GroupErrorPolicy);
    }

    // ── Deserialization error action stored and retrievable ──

    [Fact]
    public void GivenDeserializationErrorAction_WhenStored_ThenRetrievable()
    {
        // Arrange
        var registration = new ConsumerGroupRegistration<string, string>
        {
            TopicName = "orders",
            GroupId = "group-1",
            DestinationAddress = new Uri("kafka://broker:9092/kafka/test-topic"),
            BuildConsumerPipelines = () => [],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            DeserializationErrorAction = ErrorAction.Discard(),
        };

        // Assert
        Assert.NotNull(registration.DeserializationErrorAction);
        Assert.IsType<ErrorAction.DiscardAction>(registration.DeserializationErrorAction);
    }

    // ── DeadLetter deserialization action resolves to DeadLetterAction ──

    [Fact]
    public void GivenDeserializationDeadLetter_WhenConfigured_ThenReturnsDeadLetterAction()
    {
        // Arrange
        var actionBuilder = new ErrorActionBuilder();
        actionBuilder.DeadLetter();

        // Act
        var action = actionBuilder.Build();

        // Assert
        Assert.IsType<ErrorAction.DeadLetterAction>(action);
    }

    // ── DeadLetter DeserializationErrorAction stored on registration ──

    [Fact]
    public void GivenDeadLetterDeserializationErrorAction_WhenStored_ThenRetrievable()
    {
        // Arrange
        var registration = new ConsumerGroupRegistration<string, string>
        {
            TopicName = "orders",
            GroupId = "group-1",
            DestinationAddress = new Uri("kafka://broker:9092/kafka/test-topic"),
            BuildConsumerPipelines = () => [],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = _ => { },
            ApplyConsumerConfigOverrides = _ => { },
            DeserializationErrorAction = ErrorAction.DeadLetter(),
        };

        // Assert
        Assert.NotNull(registration.DeserializationErrorAction);
        Assert.IsType<ErrorAction.DeadLetterAction>(registration.DeserializationErrorAction);
    }

    // ── ErrorPolicy Default called twice throws ──

    [Fact]
    public void GivenDefaultCalledTwice_WhenBuilding_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var policyBuilder = new ErrorPolicyBuilder();
        policyBuilder.Default(a => a.Discard());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => policyBuilder.Default(a => a.Retry(1, Backoff.None).Discard()));
        Assert.Contains("already been configured", ex.Message);
    }


    // ── OnDeserializationError with DeadLetter succeeds ──

    [Fact]
    public void GivenOnDeserializationErrorWithDeadLetter_WhenConfigured_ThenSucceeds()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.OnDeserializationError(err => err.DeadLetter());

        // Assert
        Assert.NotNull(builder.DeserializationErrorAction);
    }

    // ── OnDeserializationError with Discard succeeds ──

    [Fact]
    public void GivenOnDeserializationErrorWithDiscard_WhenConfigured_ThenSucceeds()
    {
        // Arrange
        var builder = new KafkaConsumerGroupBuilder<string, string>();

        // Act
        builder.OnDeserializationError(err => err.Discard());

        // Assert
        Assert.NotNull(builder.DeserializationErrorAction);
    }
}
