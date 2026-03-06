namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Kafka.Consumer;
using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class ConsumerGroupRegistrationTests
{
    private static ConsumerGroupRegistration<string, string> CreateRegistration(
        Action<ConfluentKafka.ClientConfig>? applyClientConfig = null,
        Action<ConfluentKafka.ConsumerConfig>? applyOverrides = null,
        string groupId = "test-group",
        string topicName = "test-topic")
    {
        return new ConsumerGroupRegistration<string, string>
        {
            TopicName = topicName,
            GroupId = groupId,
            BuildConsumerPipelines = () => [],
            WorkerCount = 1,
            WorkerDistribution = WorkerDistribution.ByKeyHash,
            BufferSize = 32,
            CommitInterval = TimeSpan.FromSeconds(5),
            WorkerStopTimeout = TimeSpan.FromSeconds(30),
            ApplyClientConfig = applyClientConfig ?? (_ => { }),
            ApplyConsumerConfigOverrides = applyOverrides ?? (_ => { }),
            DeadLetterTopicMap = DeadLetterTopicMap.Empty,
        };
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenCalled_ThenAppliesClientConfigFirst()
    {
        // Arrange
        var registration = CreateRegistration(
            applyClientConfig: config => config.BootstrapServers = "localhost:9092");

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.Equal("localhost:9092", result.BootstrapServers);
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenCalled_ThenAppliesConsumerConfigOverridesSecond()
    {
        // Arrange
        var registration = CreateRegistration(
            applyOverrides: config => config.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest);

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.Equal(ConfluentKafka.AutoOffsetReset.Earliest, result.AutoOffsetReset);
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenCalled_ThenSetsEnableAutoCommitFalse()
    {
        // Arrange
        var registration = CreateRegistration();

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.False(result.EnableAutoCommit);
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenCalled_ThenSetsEnableAutoOffsetStoreFalse()
    {
        // Arrange
        var registration = CreateRegistration();

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.False(result.EnableAutoOffsetStore);
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenOverrideSetsEnableAutoCommitTrue_ThenStillFalse()
    {
        // Arrange
        var registration = CreateRegistration(
            applyOverrides: config => config.EnableAutoCommit = true);

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.False(result.EnableAutoCommit);
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenCalled_ThenSetsGroupId()
    {
        // Arrange
        var registration = CreateRegistration(groupId: "test-group");

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.Equal("test-group", result.GroupId);
    }

    [Fact]
    public void GivenBuildConsumerConfig_WhenLayeringConflict_ThenOverrideWinsOverClientConfig()
    {
        // Arrange
        var registration = CreateRegistration(
            applyClientConfig: config => config.SocketKeepaliveEnable = false,
            applyOverrides: config => config.SocketKeepaliveEnable = true);

        // Act
        var result = registration.BuildConsumerConfig();

        // Assert
        Assert.True(result.SocketKeepaliveEnable);
    }
}
