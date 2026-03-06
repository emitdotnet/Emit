namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Kafka.Consumer;
using Xunit;

public sealed class ByKeyHashStrategyTests
{
    [Fact]
    public void GivenNullKeyBytes_WhenSelectWorker_ThenReturnsWorkerZero()
    {
        // Arrange
        var strategy = new ByKeyHashStrategy(workerCount: 4);

        // Act
        var result = strategy.SelectWorker(null, partition: 0);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void GivenEmptyKeyBytes_WhenSelectWorker_ThenReturnsWorkerZero()
    {
        // Arrange
        var strategy = new ByKeyHashStrategy(workerCount: 4);

        // Act
        var result = strategy.SelectWorker([], partition: 0);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void GivenSameKeyBytes_WhenSelectWorkerMultipleTimes_ThenAlwaysReturnsSameWorker()
    {
        // Arrange
        var strategy = new ByKeyHashStrategy(workerCount: 4);
        var keyBytes = "test-key"u8.ToArray();
        var results = new List<int>();

        // Act
        for (var i = 0; i < 100; i++)
        {
            results.Add(strategy.SelectWorker(keyBytes, partition: 0));
        }

        // Assert
        Assert.Single(results.Distinct());
    }

    [Fact]
    public void GivenDifferentKeyBytes_WhenSelectWorker_ThenDistributesAcrossWorkers()
    {
        // Arrange
        var strategy = new ByKeyHashStrategy(workerCount: 4);
        var results = new HashSet<int>();

        // Act
        for (var i = 0; i < 100; i++)
        {
            var keyBytes = System.Text.Encoding.UTF8.GetBytes($"key-{i}");
            results.Add(strategy.SelectWorker(keyBytes, partition: 0));
        }

        // Assert
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public void GivenSelectWorker_WhenCalled_ThenReturnsValueInRange()
    {
        // Arrange
        var workerCount = 4;
        var strategy = new ByKeyHashStrategy(workerCount);

        // Act & Assert
        for (var i = 0; i < 100; i++)
        {
            var keyBytes = System.Text.Encoding.UTF8.GetBytes($"key-{i}");
            var result = strategy.SelectWorker(keyBytes, partition: 0);
            Assert.InRange(result, 0, workerCount - 1);
        }
    }

    [Fact]
    public void GivenSingleWorker_WhenSelectWorker_ThenAlwaysReturnsZero()
    {
        // Arrange
        var strategy = new ByKeyHashStrategy(workerCount: 1);

        // Act & Assert
        for (var i = 0; i < 100; i++)
        {
            var keyBytes = System.Text.Encoding.UTF8.GetBytes($"key-{i}");
            var result = strategy.SelectWorker(keyBytes, partition: 0);
            Assert.Equal(0, result);
        }
    }

    [Fact]
    public void GivenSameKeySamePartitionDifferentPartitions_WhenSelectWorker_ThenSameWorkerRegardlessOfPartition()
    {
        // Arrange
        var strategy = new ByKeyHashStrategy(workerCount: 4);
        var keyBytes = "test-key"u8.ToArray();

        // Act
        var result0 = strategy.SelectWorker(keyBytes, partition: 0);
        var result1 = strategy.SelectWorker(keyBytes, partition: 1);
        var result2 = strategy.SelectWorker(keyBytes, partition: 2);

        // Assert
        Assert.Equal(result0, result1);
        Assert.Equal(result1, result2);
    }
}
