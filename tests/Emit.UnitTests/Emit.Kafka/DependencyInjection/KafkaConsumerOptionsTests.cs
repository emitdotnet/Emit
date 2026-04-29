namespace Emit.Kafka.Tests.DependencyInjection;

using System.Collections.Generic;
using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaConsumerOptionsTests
{
    [Fact]
    public void GivenAutoOffsetResetSet_WhenApplyTo_ThenConfigAutoOffsetResetIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions { AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest };
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
        var consumerConfig = new KafkaConsumerOptions { SessionTimeout = TimeSpan.FromSeconds(10) };
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
        var consumerConfig = new KafkaConsumerOptions { HeartbeatInterval = TimeSpan.FromSeconds(3) };
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
        var consumerConfig = new KafkaConsumerOptions { MaxPollInterval = TimeSpan.FromMinutes(5) };
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
        var consumerConfig = new KafkaConsumerOptions
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
        var consumerConfig = new KafkaConsumerOptions { GroupInstanceId = "instance-1" };
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
        var consumerConfig = new KafkaConsumerOptions
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
        var consumerConfig = new KafkaConsumerOptions { IsolationLevel = ConfluentKafka.IsolationLevel.ReadCommitted };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.IsolationLevel.ReadCommitted, config.IsolationLevel);
    }

    [Fact]
    public void GivenCheckCrcsSet_WhenApplyTo_ThenConfigCheckCrcsIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions { CheckCrcs = true };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(true, config.CheckCrcs);
    }

    [Fact]
    public void GivenGroupProtocolSet_WhenApplyTo_ThenConfigGroupProtocolIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions { GroupProtocol = ConfluentKafka.GroupProtocol.Consumer };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.GroupProtocol.Consumer, config.GroupProtocol);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenConsumerConfigUnchanged()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions();
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
    public void GivenQueuedMaxMessagesKbytesSet_WhenApplyTo_ThenConfigQueuedMaxMessagesKbytesIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions { QueuedMaxMessagesKbytes = 131072 };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(131072, config.QueuedMaxMessagesKbytes);
    }

    [Fact]
    public void GivenQueuedMinMessagesSet_WhenApplyTo_ThenConfigQueuedMinMessagesIsSet()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions { QueuedMinMessages = 50000 };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(50000, config.QueuedMinMessages);
    }

    [Fact]
    public void GivenAdditionalPropertiesSet_WhenApplyTo_ThenRawPropertiesApplied()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions
        {
            AdditionalProperties = new Dictionary<string, string>
            {
                ["fetch.error.backoff.ms"] = "250"
            }
        };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal("250", config.Get("fetch.error.backoff.ms"));
    }

    [Fact]
    public void GivenAdditionalPropertyConflictsWithTypedProperty_WhenApplyTo_ThenTypedPropertyWins()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions
        {
            FetchMaxBytes = 1000000,
            AdditionalProperties = new Dictionary<string, string>
            {
                ["fetch.max.bytes"] = "9999999"
            }
        };
        var config = new ConfluentKafka.ConsumerConfig();

        // Act
        consumerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(1000000, config.FetchMaxBytes);
    }

    [Fact]
    public void GivenAllPropertiesSet_WhenApplyTo_ThenAllApplied()
    {
        // Arrange
        var consumerConfig = new KafkaConsumerOptions
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
            IsolationLevel = ConfluentKafka.IsolationLevel.ReadCommitted,
            CheckCrcs = true,
            GroupProtocol = ConfluentKafka.GroupProtocol.Consumer,
            QueuedMaxMessagesKbytes = 131072,
            QueuedMinMessages = 50000
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
        Assert.Equal(true, config.CheckCrcs);
        Assert.Equal(ConfluentKafka.GroupProtocol.Consumer, config.GroupProtocol);
        Assert.Equal(131072, config.QueuedMaxMessagesKbytes);
        Assert.Equal(50000, config.QueuedMinMessages);
    }
}
