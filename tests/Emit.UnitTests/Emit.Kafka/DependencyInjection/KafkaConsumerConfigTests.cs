namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaConsumerConfigTests
{
    [Fact]
    public void GivenAutoOffsetResetSet_WhenApplyTo_ThenConfigAutoOffsetResetIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig { AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.AutoOffsetReset.Earliest, config.AutoOffsetReset);
    }

    [Fact]
    public void GivenSessionTimeoutSet_WhenApplyTo_ThenConfigSessionTimeoutMsIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig { SessionTimeout = TimeSpan.FromSeconds(10) };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(10000, config.SessionTimeoutMs);
    }

    [Fact]
    public void GivenHeartbeatIntervalSet_WhenApplyTo_ThenConfigHeartbeatIntervalMsIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig { HeartbeatInterval = TimeSpan.FromSeconds(3) };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(3000, config.HeartbeatIntervalMs);
    }

    [Fact]
    public void GivenMaxPollIntervalSet_WhenApplyTo_ThenConfigMaxPollIntervalMsIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig { MaxPollInterval = TimeSpan.FromMinutes(5) };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(300000, config.MaxPollIntervalMs);
    }

    [Fact]
    public void GivenFetchPropertiesSet_WhenApplyTo_ThenAllFetchPropertiesApplied()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig
        {
            FetchMinBytes = 1024,
            FetchMaxBytes = 52428800,
            FetchWaitMax = TimeSpan.FromMilliseconds(500),
            MaxPartitionFetchBytes = 1048576
        };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(1024, config.FetchMinBytes);
        Assert.Equal(52428800, config.FetchMaxBytes);
        Assert.Equal(500, config.FetchWaitMaxMs);
        Assert.Equal(1048576, config.MaxPartitionFetchBytes);
    }

    [Fact]
    public void GivenGroupInstanceIdSet_WhenApplyTo_ThenConfigGroupInstanceIdIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig { GroupInstanceId = "instance-1" };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal("instance-1", config.GroupInstanceId);
    }

    [Fact]
    public void GivenPartitionAssignmentStrategySet_WhenApplyTo_ThenConfigPartitionAssignmentStrategyIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig
        {
            PartitionAssignmentStrategy = ConfluentKafka.PartitionAssignmentStrategy.RoundRobin
        };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.PartitionAssignmentStrategy.RoundRobin, config.PartitionAssignmentStrategy);
    }

    [Fact]
    public void GivenIsolationLevelSet_WhenApplyTo_ThenConfigIsolationLevelIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig { IsolationLevel = ConfluentKafka.IsolationLevel.ReadCommitted };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.IsolationLevel.ReadCommitted, config.IsolationLevel);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenConsumerConfigUnchanged()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig();
        var config = new ConfluentKafka.ConsumerConfig
        {
            AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Latest,
            SessionTimeoutMs = 6000
        };

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.AutoOffsetReset.Latest, config.AutoOffsetReset);
        Assert.Equal(6000, config.SessionTimeoutMs);
    }

    [Fact]
    public void GivenAllPropertiesSet_WhenApplyTo_ThenAllApplied()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerConfig
        {
            AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest,
            SessionTimeout = TimeSpan.FromSeconds(10),
            HeartbeatInterval = TimeSpan.FromSeconds(3),
            MaxPollInterval = TimeSpan.FromMinutes(5),
            FetchMinBytes = 1024,
            FetchMaxBytes = 52428800,
            FetchWaitMax = TimeSpan.FromMilliseconds(500),
            MaxPartitionFetchBytes = 1048576,
            GroupInstanceId = "instance-1",
            PartitionAssignmentStrategy = ConfluentKafka.PartitionAssignmentStrategy.RoundRobin,
            IsolationLevel = ConfluentKafka.IsolationLevel.ReadCommitted
        };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal(10000, config.SessionTimeoutMs);
        Assert.Equal(3000, config.HeartbeatIntervalMs);
        Assert.Equal(300000, config.MaxPollIntervalMs);
        Assert.Equal(1024, config.FetchMinBytes);
        Assert.Equal(52428800, config.FetchMaxBytes);
        Assert.Equal(500, config.FetchWaitMaxMs);
        Assert.Equal(1048576, config.MaxPartitionFetchBytes);
        Assert.Equal("instance-1", config.GroupInstanceId);
        Assert.Equal(ConfluentKafka.PartitionAssignmentStrategy.RoundRobin, config.PartitionAssignmentStrategy);
        Assert.Equal(ConfluentKafka.IsolationLevel.ReadCommitted, config.IsolationLevel);
    }
}
