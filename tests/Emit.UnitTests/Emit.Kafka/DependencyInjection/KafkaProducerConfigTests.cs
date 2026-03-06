namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaProducerConfigTests
{
    [Fact]
    public void GivenAcksSet_WhenApplyTo_ThenProducerConfigAcksIsSet()
    {
        // Arrange
        var producerConfig = new KafkaProducerConfig { Acks = ConfluentKafka.Acks.All };
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
        var producerConfig = new KafkaProducerConfig { Linger = TimeSpan.FromMilliseconds(50) };
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
        var producerConfig = new KafkaProducerConfig { BatchSize = 65536 };
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
        var producerConfig = new KafkaProducerConfig { EnableIdempotence = true };
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
        var producerConfig = new KafkaProducerConfig { CompressionType = ConfluentKafka.CompressionType.Snappy };
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
        var producerConfig = new KafkaProducerConfig { BatchNumMessages = 10000 };
        var config = new ConfluentKafka.ProducerConfig();

        // Act
        producerConfig.ApplyTo(config);

        // Assert
        Assert.Equal(10000, config.BatchNumMessages);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenProducerConfigUnchanged()
    {
        // Arrange
        var producerConfig = new KafkaProducerConfig();
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
        var producerConfig = new KafkaProducerConfig
        {
            Acks = ConfluentKafka.Acks.All,
            Linger = TimeSpan.FromMilliseconds(50),
            BatchSize = 65536,
            EnableIdempotence = true,
            CompressionType = ConfluentKafka.CompressionType.Snappy,
            BatchNumMessages = 10000
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
    }
}
