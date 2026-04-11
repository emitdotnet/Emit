namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaProducerOptionsTests
{
    [Fact]
    public void GivenAcksSet_WhenApplyTo_ThenProducerConfigAcksIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { Acks = ConfluentKafka.Acks.All };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.Acks.All, config.Acks);
    }

    [Fact]
    public void GivenLingerSet_WhenApplyTo_ThenProducerConfigLingerMsIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { Linger = TimeSpan.FromMilliseconds(50) };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(50, config.LingerMs);
    }

    [Fact]
    public void GivenBatchSizeSet_WhenApplyTo_ThenProducerConfigBatchSizeIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { BatchSize = 65536 };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(65536, config.BatchSize);
    }

    [Fact]
    public void GivenEnableIdempotenceSet_WhenApplyTo_ThenProducerConfigEnableIdempotenceIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { EnableIdempotence = true };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.True(config.EnableIdempotence);
    }

    [Fact]
    public void GivenCompressionTypeSet_WhenApplyTo_ThenProducerConfigCompressionTypeIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { CompressionType = ConfluentKafka.CompressionType.Snappy };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.CompressionType.Snappy, config.CompressionType);
    }

    [Fact]
    public void GivenBatchNumMessagesSet_WhenApplyTo_ThenProducerConfigBatchNumMessagesIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { BatchNumMessages = 10000 };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(10000, config.BatchNumMessages);
    }

    [Fact]
    public void GivenMessageTimeoutSet_WhenApplyTo_ThenProducerConfigMessageTimeoutMsIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { MessageTimeout = TimeSpan.FromMinutes(5) };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(300000, config.MessageTimeoutMs);
    }

    [Fact]
    public void GivenRequestTimeoutSet_WhenApplyTo_ThenProducerConfigRequestTimeoutMsIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { RequestTimeout = TimeSpan.FromSeconds(30) };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(30000, config.RequestTimeoutMs);
    }

    [Fact]
    public void GivenTransactionTimeoutSet_WhenApplyTo_ThenProducerConfigTransactionTimeoutMsIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { TransactionTimeout = TimeSpan.FromMinutes(1) };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(60000, config.TransactionTimeoutMs);
    }

    [Fact]
    public void GivenTransactionalIdSet_WhenApplyTo_ThenProducerConfigTransactionalIdIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { TransactionalId = "my-txn-id" };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal("my-txn-id", config.TransactionalId);
    }

    [Fact]
    public void GivenQueueBufferingMaxMessagesSet_WhenApplyTo_ThenProducerConfigQueueBufferingMaxMessagesIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { QueueBufferingMaxMessages = 100000 };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(100000, config.QueueBufferingMaxMessages);
    }

    [Fact]
    public void GivenQueueBufferingMaxKbytesSet_WhenApplyTo_ThenProducerConfigQueueBufferingMaxKbytesIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { QueueBufferingMaxKbytes = 1048576 };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(1048576, config.QueueBufferingMaxKbytes);
    }

    [Fact]
    public void GivenPartitionerSet_WhenApplyTo_ThenProducerConfigPartitionerIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { Partitioner = ConfluentKafka.Partitioner.Murmur2Random };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.Partitioner.Murmur2Random, config.Partitioner);
    }

    [Fact]
    public void GivenMessageSendMaxRetriesSet_WhenApplyTo_ThenProducerConfigMessageSendMaxRetriesIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions { MessageSendMaxRetries = 3 };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(3, config.MessageSendMaxRetries);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenProducerConfigUnchanged()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions();
        var config = new ConfluentKafka.ProducerConfig
        {
            Acks = ConfluentKafka.Acks.Leader,
            LingerMs = 10,
        };

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.Acks.Leader, config.Acks);
        Assert.Equal(10, config.LingerMs); // Confluent's property, stays as int
    }

    [Fact]
    public void GivenAllPropertiesSet_WhenApplyTo_ThenAllApplied()
    {
        // Arrange
        var producerConfig = new KafkaProducerOptions
        {
            Acks = ConfluentKafka.Acks.All,
            Linger = TimeSpan.FromMilliseconds(50),
            BatchSize = 65536,
            EnableIdempotence = true,
            CompressionType = ConfluentKafka.CompressionType.Snappy,
            BatchNumMessages = 10000,
            MessageTimeout = TimeSpan.FromMinutes(5),
            RequestTimeout = TimeSpan.FromSeconds(30),
            TransactionTimeout = TimeSpan.FromMinutes(1),
            TransactionalId = "my-txn-id",
            QueueBufferingMaxMessages = 100000,
            QueueBufferingMaxKbytes = 1048576,
            Partitioner = ConfluentKafka.Partitioner.Murmur2Random,
            MessageSendMaxRetries = 3
        };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.Acks.All, config.Acks);
        Assert.Equal(50, config.LingerMs);
        Assert.Equal(65536, config.BatchSize);
        Assert.True(config.EnableIdempotence);
        Assert.Equal(ConfluentKafka.CompressionType.Snappy, config.CompressionType);
        Assert.Equal(10000, config.BatchNumMessages);
        Assert.Equal(300000, config.MessageTimeoutMs);
        Assert.Equal(30000, config.RequestTimeoutMs);
        Assert.Equal(60000, config.TransactionTimeoutMs);
        Assert.Equal("my-txn-id", config.TransactionalId);
        Assert.Equal(100000, config.QueueBufferingMaxMessages);
        Assert.Equal(1048576, config.QueueBufferingMaxKbytes);
        Assert.Equal(ConfluentKafka.Partitioner.Murmur2Random, config.Partitioner);
        Assert.Equal(3, config.MessageSendMaxRetries);
    }
}
