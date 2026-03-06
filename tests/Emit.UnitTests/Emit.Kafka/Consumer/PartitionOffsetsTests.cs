namespace Emit.Kafka.Tests.Consumer;

using global::Emit.Kafka.Consumer;
using Xunit;

public sealed class PartitionOffsetsTests
{
    [Fact]
    public void GivenSingleOffset_WhenEnqueuedAndProcessed_ThenWatermarkAdvances()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);

        // Act
        var watermark = po.MarkAsProcessed(5);

        // Assert
        Assert.Equal(5, watermark);
    }

    [Fact]
    public void GivenInOrderCompletion_WhenAllProcessedSequentially_ThenWatermarkAdvancesEachTime()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);
        po.Enqueue(6);
        po.Enqueue(7);
        po.Enqueue(8);
        po.Enqueue(9);

        // Act & Assert
        Assert.Equal(5, po.MarkAsProcessed(5));
        Assert.Equal(6, po.MarkAsProcessed(6));
        Assert.Equal(7, po.MarkAsProcessed(7));
        Assert.Equal(8, po.MarkAsProcessed(8));
        Assert.Equal(9, po.MarkAsProcessed(9));
    }

    [Fact]
    public void GivenOutOfOrderCompletion_WhenNonHeadCompleted_ThenWatermarkDoesNotAdvance()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);
        po.Enqueue(6);
        po.Enqueue(7);

        // Act
        var watermark = po.MarkAsProcessed(7);

        // Assert
        Assert.Null(watermark);
    }

    [Fact]
    public void GivenOutOfOrderCompletion_WhenHeadCompletedAfterwards_ThenWatermarkJumpsForward()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);
        po.Enqueue(6);
        po.Enqueue(7);
        po.Enqueue(8);

        // Act
        var watermark1 = po.MarkAsProcessed(7);
        var watermark2 = po.MarkAsProcessed(6);
        var watermark3 = po.MarkAsProcessed(5);

        // Assert
        Assert.Null(watermark1);
        Assert.Null(watermark2);
        Assert.Equal(7, watermark3);
    }

    [Fact]
    public void GivenFullWalkthrough_WhenFiveMessagesThreeWorkers_ThenWatermarkMatchesPRDExample()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);
        po.Enqueue(6);
        po.Enqueue(7);
        po.Enqueue(8);
        po.Enqueue(9);

        // Act
        var w1 = po.MarkAsProcessed(7);
        var w2 = po.MarkAsProcessed(8);
        var w3 = po.MarkAsProcessed(5);
        var w4 = po.MarkAsProcessed(6);
        var w5 = po.MarkAsProcessed(9);

        // Assert
        Assert.Null(w1);
        Assert.Null(w2);
        Assert.Equal(5, w3);
        Assert.Equal(8, w4);
        Assert.Equal(9, w5);
    }

    [Fact]
    public void GivenGapInOffsets_WhenCompactedTopic_ThenWatermarkStillAdvances()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);
        po.Enqueue(8);
        po.Enqueue(9);

        // Act
        var w1 = po.MarkAsProcessed(5);
        var w2 = po.MarkAsProcessed(9);
        var w3 = po.MarkAsProcessed(8);

        // Assert
        Assert.Equal(5, w1);
        Assert.Null(w2);
        Assert.Equal(9, w3);
    }

    [Fact]
    public void GivenEmptyState_WhenMarkAsProcessed_ThenReturnsNull()
    {
        // Arrange
        var po = new PartitionOffsets();

        // Act
        var watermark = po.MarkAsProcessed(5);

        // Assert
        Assert.Null(watermark);
    }

    [Fact]
    public void GivenStuckHead_WhenSubsequentOffsetsComplete_ThenWatermarkDoesNotAdvance()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(5);
        po.Enqueue(6);
        po.Enqueue(7);
        po.Enqueue(8);
        po.Enqueue(9);

        // Act
        var w1 = po.MarkAsProcessed(6);
        var w2 = po.MarkAsProcessed(7);
        var w3 = po.MarkAsProcessed(8);
        var w4 = po.MarkAsProcessed(9);

        // Assert
        Assert.Null(w1);
        Assert.Null(w2);
        Assert.Null(w3);
        Assert.Null(w4);
    }

    [Fact]
    public void GivenAllCompletedOutOfOrder_WhenHeadFinallyCompletes_ThenWatermarkJumpsToEnd()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(10);
        po.Enqueue(11);
        po.Enqueue(12);
        po.Enqueue(13);
        po.Enqueue(14);

        // Act
        var w1 = po.MarkAsProcessed(14);
        var w2 = po.MarkAsProcessed(13);
        var w3 = po.MarkAsProcessed(12);
        var w4 = po.MarkAsProcessed(11);
        var w5 = po.MarkAsProcessed(10);

        // Assert
        Assert.Null(w1);
        Assert.Null(w2);
        Assert.Null(w3);
        Assert.Null(w4);
        Assert.Equal(14, w5);
    }

    [Fact]
    public void GivenLargeNumberOfOffsets_WhenMixedOrderCompletion_ThenWatermarkCorrect()
    {
        // Arrange
        var po = new PartitionOffsets();
        for (long i = 0; i < 256; i++)
        {
            po.Enqueue(i);
        }

        var rng = new Random(42);
        var offsets = Enumerable.Range(0, 256).Select(i => (long)i).ToArray();
        var shuffled = offsets.OrderBy(_ => rng.Next()).ToArray();

        // Act
        long? finalWatermark = null;
        foreach (var offset in shuffled)
        {
            var watermark = po.MarkAsProcessed(offset);
            if (watermark.HasValue)
            {
                finalWatermark = watermark;
            }
        }

        // Assert
        Assert.Equal(255, finalWatermark);
    }

    [Fact]
    public void GivenPartialCompletion_WhenHeadGroupCompleted_ThenWatermarkAdvancesPartially()
    {
        // Arrange
        var po = new PartitionOffsets();
        po.Enqueue(1);
        po.Enqueue(2);
        po.Enqueue(3);
        po.Enqueue(4);
        po.Enqueue(5);

        // Act
        var w1 = po.MarkAsProcessed(3);
        var w2 = po.MarkAsProcessed(1);
        var w3 = po.MarkAsProcessed(2);

        // Assert
        Assert.Null(w1);
        Assert.Equal(1, w2);
        Assert.Equal(3, w3);
    }

    [Fact]
    public async Task GivenConcurrentAccess_WhenEnqueueAndMarkAsProcessed_ThenThreadSafe()
    {
        // Arrange
        var po = new PartitionOffsets();
        using var barrier = new Barrier(2);

        var enqueueTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (long i = 0; i < 1000; i++)
            {
                po.Enqueue(i);
            }
        });
        var processTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (long i = 0; i < 1000; i++)
            {
                po.MarkAsProcessed(i);
            }
        });

        // Act & Assert - no exceptions
        await Task.WhenAll(enqueueTask, processTask);
    }
}
