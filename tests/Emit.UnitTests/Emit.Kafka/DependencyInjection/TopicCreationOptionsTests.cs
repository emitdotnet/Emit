namespace Emit.Kafka.Tests.DependencyInjection;

using System.Globalization;
using global::Emit.Kafka.DependencyInjection;
using Xunit;

public sealed class TopicCreationOptionsTests
{
    [Fact]
    public void GivenDefaults_WhenCreated_ThenMatchesKafkaDefaults()
    {
        // Arrange & Act
        var options = new TopicCreationOptions();

        // Assert
        Assert.Null(options.NumPartitions);
        Assert.Null(options.ReplicationFactor);
        Assert.Equal(TimeSpan.FromDays(7), options.Retention);
        Assert.Null(options.RetentionBytes);
        Assert.Equal(TopicCleanupPolicy.Delete, options.CleanupPolicy);
        Assert.Equal(TimeSpan.FromDays(1), options.DeleteRetention);
        Assert.Equal(TopicCompressionType.Producer, options.CompressionType);
        Assert.Equal(-1, options.GzipCompressionLevel);
        Assert.Equal(9, options.Lz4CompressionLevel);
        Assert.Equal(3, options.ZstdCompressionLevel);
        Assert.Equal(0.5, options.MinCleanableDirtyRatio);
        Assert.Equal(TimeSpan.Zero, options.MinCompactionLag);
        Assert.Null(options.MaxCompactionLag);
    }

    [Fact]
    public void GivenDefaultOptions_WhenBuildSpecification_ThenConfigsMatchKafkaDefaults()
    {
        // Arrange
        var options = new TopicCreationOptions();

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal(((long)TimeSpan.FromDays(7).TotalMilliseconds).ToString(), spec.Configs["retention.ms"]);
        Assert.Equal("-1", spec.Configs["retention.bytes"]);
        Assert.Equal("delete", spec.Configs["cleanup.policy"]);
        Assert.Equal(((long)TimeSpan.FromDays(1).TotalMilliseconds).ToString(), spec.Configs["delete.retention.ms"]);
        Assert.Equal("producer", spec.Configs["compression.type"]);
        Assert.Equal("0.50", spec.Configs["min.cleanable.dirty.ratio"]);
        Assert.Equal("0", spec.Configs["min.compaction.lag.ms"]);
        Assert.False(spec.Configs.ContainsKey("max.compaction.lag.ms"));
    }

    [Fact]
    public void GivenNullRetention_WhenBuildSpecification_ThenRetentionMsIsNegativeOne()
    {
        // Arrange
        var options = new TopicCreationOptions { Retention = null };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal("-1", spec.Configs["retention.ms"]);
    }

    [Fact]
    public void GivenCustomRetention_WhenBuildSpecification_ThenRetentionMsIsMilliseconds()
    {
        // Arrange
        var options = new TopicCreationOptions { Retention = TimeSpan.FromHours(2) };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal("7200000", spec.Configs["retention.ms"]);
    }

    [Fact]
    public void GivenNullRetentionBytes_WhenBuildSpecification_ThenRetentionBytesIsNegativeOne()
    {
        // Arrange
        var options = new TopicCreationOptions { RetentionBytes = null };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal("-1", spec.Configs["retention.bytes"]);
    }

    [Fact]
    public void GivenNullMaxCompactionLag_WhenBuildSpecification_ThenKeyOmitted()
    {
        // Arrange
        var options = new TopicCreationOptions { MaxCompactionLag = null };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.False(spec.Configs.ContainsKey("max.compaction.lag.ms"));
    }

    [Fact]
    public void GivenCustomMaxCompactionLag_WhenBuildSpecification_ThenKeyPresent()
    {
        // Arrange
        var options = new TopicCreationOptions { MaxCompactionLag = TimeSpan.FromHours(1) };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.True(spec.Configs.ContainsKey("max.compaction.lag.ms"));
        Assert.Equal("3600000", spec.Configs["max.compaction.lag.ms"]);
    }

    [Fact]
    public void GivenNullNumPartitions_WhenBuildSpecification_ThenNegativeOneOnSpec()
    {
        // Arrange
        var options = new TopicCreationOptions { NumPartitions = null };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal(-1, spec.NumPartitions);
    }

    [Fact]
    public void GivenNullReplicationFactor_WhenBuildSpecification_ThenNegativeOneOnSpec()
    {
        // Arrange
        var options = new TopicCreationOptions { ReplicationFactor = null };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal(-1, spec.ReplicationFactor);
    }

    [Fact]
    public void GivenCompactCleanupPolicy_WhenBuildSpecification_ThenConfigIsCompact()
    {
        // Arrange
        var options = new TopicCreationOptions { CleanupPolicy = TopicCleanupPolicy.Compact };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal("compact", spec.Configs["cleanup.policy"]);
    }

    [Fact]
    public void GivenDeleteAndCompactCleanupPolicy_WhenBuildSpecification_ThenConfigIsDeleteCommaCompact()
    {
        // Arrange
        var options = new TopicCreationOptions { CleanupPolicy = TopicCleanupPolicy.DeleteAndCompact };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal("delete,compact", spec.Configs["cleanup.policy"]);
    }

    [Fact]
    public void GivenCompressionTypeGzip_WhenBuildSpecification_ThenConfigIsGzip()
    {
        // Arrange
        var options = new TopicCreationOptions { CompressionType = TopicCompressionType.Gzip };

        // Act
        var spec = options.BuildSpecification("my-topic");

        // Assert
        Assert.Equal("gzip", spec.Configs["compression.type"]);
        Assert.True(spec.Configs.ContainsKey("compression.gzip.level"));
        Assert.Equal(options.GzipCompressionLevel.ToString(), spec.Configs["compression.gzip.level"]);
    }

    [Fact]
    public void GivenFrenchCulture_WhenBuildSpecification_ThenNumericConfigsUseDotDecimalSeparator()
    {
        // Arrange
        var options = new TopicCreationOptions { MinCleanableDirtyRatio = 0.5 };
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

        try
        {
            // Act
            var spec = options.BuildSpecification("my-topic");

            // Assert
            Assert.Equal("0.50", spec.Configs["min.cleanable.dirty.ratio"]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void GivenTopicName_WhenBuildSpecification_ThenSpecificationHasCorrectName()
    {
        // Arrange
        var options = new TopicCreationOptions();

        // Act
        var spec = options.BuildSpecification("events.orders.created");

        // Assert
        Assert.Equal("events.orders.created", spec.Name);
    }
}
