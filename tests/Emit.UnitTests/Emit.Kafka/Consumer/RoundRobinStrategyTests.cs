namespace Emit.Kafka.Tests.Consumer;

using System.Reflection;
using global::Emit.Kafka.Consumer;
using Xunit;

public sealed class RoundRobinStrategyTests
{
    [Fact]
    public void GivenRoundRobin_WhenSelectWorkerCalledSequentially_ThenCyclesThroughWorkers()
    {
        // Arrange
        var strategy = new RoundRobinStrategy(workerCount: 3);

        // Act
        var results = new List<int>
        {
            strategy.SelectWorker(null, partition: 0),
            strategy.SelectWorker(null, partition: 0),
            strategy.SelectWorker(null, partition: 0),
            strategy.SelectWorker(null, partition: 0),
            strategy.SelectWorker(null, partition: 0),
            strategy.SelectWorker(null, partition: 0)
        };

        // Assert
        Assert.Equal([0, 1, 2, 0, 1, 2], results);
    }

    [Fact]
    public void GivenRoundRobin_WhenSelectWorker_ThenReturnsValueInRange()
    {
        // Arrange
        var workerCount = 4;
        var strategy = new RoundRobinStrategy(workerCount);

        // Act & Assert
        for (var i = 0; i < 100; i++)
        {
            var result = strategy.SelectWorker(null, partition: 0);
            Assert.InRange(result, 0, workerCount - 1);
        }
    }

    [Fact]
    public void GivenRoundRobin_WhenSelectWorker_ThenIgnoresKeyBytes()
    {
        // Arrange
        var strategy = new RoundRobinStrategy(workerCount: 2);

        // Act
        var result1 = strategy.SelectWorker(null, partition: 0);
        var result2 = strategy.SelectWorker("key1"u8.ToArray(), partition: 0);
        var result3 = strategy.SelectWorker("key2"u8.ToArray(), partition: 0);
        var result4 = strategy.SelectWorker(null, partition: 0);

        // Assert
        Assert.Equal([0, 1, 0, 1], new[] { result1, result2, result3, result4 });
    }

    [Fact]
    public void GivenRoundRobin_WhenSelectWorker_ThenIgnoresPartition()
    {
        // Arrange
        var strategy = new RoundRobinStrategy(workerCount: 3);

        // Act
        var result1 = strategy.SelectWorker(null, partition: 0);
        var result2 = strategy.SelectWorker(null, partition: 5);
        var result3 = strategy.SelectWorker(null, partition: 10);

        // Assert
        Assert.Equal([0, 1, 2], new[] { result1, result2, result3 });
    }

    [Fact]
    public void GivenSingleWorker_WhenSelectWorker_ThenAlwaysReturnsZero()
    {
        // Arrange
        var strategy = new RoundRobinStrategy(workerCount: 1);

        // Act & Assert
        for (var i = 0; i < 100; i++)
        {
            var result = strategy.SelectWorker(null, partition: 0);
            Assert.Equal(0, result);
        }
    }

    [Fact]
    public void GivenRoundRobin_WhenCounterWrapsAround_ThenStillReturnsValidRange()
    {
        // Arrange
        var workerCount = 4;
        var strategy = new RoundRobinStrategy(workerCount);
        var counterField = typeof(RoundRobinStrategy).GetField("counter", BindingFlags.NonPublic | BindingFlags.Instance);
        counterField!.SetValue(strategy, int.MaxValue - 2);

        // Act & Assert
        for (var i = 0; i < 10; i++)
        {
            var result = strategy.SelectWorker(null, partition: 0);
            Assert.InRange(result, 0, workerCount - 1);
        }
    }
}
