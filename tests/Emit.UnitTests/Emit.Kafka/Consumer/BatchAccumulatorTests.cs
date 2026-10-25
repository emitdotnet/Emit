namespace Emit.Kafka.Tests.Consumer;

using System.Threading.Channels;
using global::Emit.Kafka.Consumer;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class BatchAccumulatorTests
{
    private static ConfluentKafka.ConsumeResult<byte[], byte[]> MakeResult(int offset = 0) =>
        new()
        {
            Message = new ConfluentKafka.Message<byte[], byte[]>
            {
                Key = [(byte)offset],
                Value = [(byte)(offset + 1)],
            },
            TopicPartitionOffset = new ConfluentKafka.TopicPartitionOffset("test-topic", 0, offset),
        };

    [Fact]
    public async Task Given_EmptyChannel_When_AccumulateAsync_Then_BlocksUntilFirstMessage()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>();
        var accumulator = new BatchAccumulator(10, TimeSpan.FromSeconds(1), channel.Reader);

        // Write one message after a brief delay to simulate blocking
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await channel.Writer.WriteAsync(MakeResult(0));
            channel.Writer.Complete();
        });

        // Act
        var batch = await accumulator.AccumulateAsync(CancellationToken.None);

        // Assert — batch contains the one message written after the delay
        Assert.NotNull(batch);
        Assert.Single(batch!);
    }

    [Fact]
    public async Task Given_MaxSizeMessages_When_AccumulateAsync_Then_ReturnsFullBatch()
    {
        // Arrange
        const int maxSize = 5;
        var channel = Channel.CreateUnbounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>();
        var accumulator = new BatchAccumulator(maxSize, TimeSpan.FromSeconds(30), channel.Reader);

        for (var i = 0; i < maxSize; i++)
            await channel.Writer.WriteAsync(MakeResult(i));

        // Act
        var batch = await accumulator.AccumulateAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(batch);
        Assert.Equal(maxSize, batch!.Count);
    }

    [Fact]
    public async Task Given_PartialMessages_When_TimeoutElapses_Then_ReturnsPartialBatch()
    {
        // Arrange — write 2 messages but maxSize is 10; timeout is very short
        const int maxSize = 10;
        var channel = Channel.CreateUnbounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>();
        var accumulator = new BatchAccumulator(maxSize, TimeSpan.FromMilliseconds(50), channel.Reader);

        await channel.Writer.WriteAsync(MakeResult(0));
        await channel.Writer.WriteAsync(MakeResult(1));

        // Act
        var batch = await accumulator.AccumulateAsync(CancellationToken.None);

        // Assert — returns partial batch of 2 after timeout
        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Count);
    }

    [Fact]
    public async Task Given_CancellationRequested_When_WaitingInPhase1_Then_ThrowsOperationCancelledException()
    {
        // Arrange — empty channel, cancel immediately
        var channel = Channel.CreateUnbounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>();
        var accumulator = new BatchAccumulator(10, TimeSpan.FromSeconds(30), channel.Reader);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => accumulator.AccumulateAsync(cts.Token));
    }

    [Fact]
    public async Task Given_ChannelCompleted_When_NoMessagesAccumulated_Then_ReturnsNull()
    {
        // Arrange — complete the channel before any messages
        var channel = Channel.CreateUnbounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>();
        channel.Writer.Complete();
        var accumulator = new BatchAccumulator(10, TimeSpan.FromSeconds(30), channel.Reader);

        // Act
        var batch = await accumulator.AccumulateAsync(CancellationToken.None);

        // Assert
        Assert.Null(batch);
    }

    [Fact]
    public async Task Given_CancellationRequested_When_InPhase2_Then_ReturnsAccumulatedMessages()
    {
        // Arrange: two messages taken (phase 2 entered), then cancelled while waiting for more
        var channel = Channel.CreateUnbounded<ConfluentKafka.ConsumeResult<byte[], byte[]>>();
        var accumulator = new BatchAccumulator(10, TimeSpan.FromSeconds(30), channel.Reader);
        await channel.Writer.WriteAsync(MakeResult(0));
        await channel.Writer.WriteAsync(MakeResult(1));
        using var cts = new CancellationTokenSource();

        // Act
        var accumulating = accumulator.AccumulateAsync(cts.Token);
        await cts.CancelAsync();
        var batch = await accumulating;

        // Assert: the messages taken come back, for the worker to count them as unhandled
        Assert.NotNull(batch);
        Assert.Equal([0L, 1L], batch.Select(r => r.Offset.Value));
    }
}
