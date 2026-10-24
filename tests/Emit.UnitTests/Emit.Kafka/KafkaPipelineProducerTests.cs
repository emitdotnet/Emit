namespace Emit.Kafka.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Kafka.Consumer;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public sealed class KafkaPipelineProducerTests
{
    private static readonly Uri TestDestination = EmitEndpointAddress.ForEntity("kafka", "broker", 9092, "test-topic");
    private static readonly Uri TestHost = EmitEndpointAddress.ForHost("kafka", "broker", 9092);
    private readonly FakeTimeProvider timeProvider = new(DateTimeOffset.UtcNow);

    [Fact]
    public async Task GivenNullMessage_WhenProduceAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(_ => Task.CompletedTask);
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => producer.ProduceAsync(null!));
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenPipelineInvoked()
    {
        // Arrange
        var pipelineInvoked = false;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(_ =>
        {
            pipelineInvoked = true;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);
        var message = new EventMessage<string, string>("key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.True(pipelineInvoked);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextHasCorrectMessageValue()
    {
        // Arrange
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);
        var message = new EventMessage<string, string>("key", "test-value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal("test-value", capturedContext.Message);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextHasKeyAndDestinationAddress()
    {
        // Arrange
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var destination = (Uri)EmitEndpointAddress.ForEntity("kafka", "broker", 9092, "orders");
        var producer = new KafkaPipelineProducer<string, string>(pipeline, destination, TestHost, null!, timeProvider);
        var message = new EventMessage<string, string>("order-key", "order-value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        var messageKey = capturedContext.TryGetPayload<KafkaTransportContext<string>>();
        Assert.NotNull(messageKey);
        Assert.Equal("order-key", messageKey.Key);
        Assert.Equal(destination, capturedContext.DestinationAddress);
        Assert.Equal("orders", EmitEndpointAddress.GetEntityName(capturedContext.DestinationAddress));
    }

    [Fact]
    public async Task GivenTombstone_WhenProduceAsync_ThenThePipelineReceivesItsKeyAndHeadersWithNoValue()
    {
        // Arrange: a tombstone goes through the same pipeline as any message
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);

        // Act
        await producer.ProduceAsync(EventMessage<string, string>.Tombstone("order-key", [new("reason", "deleted")]));

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Null(capturedContext.Message);
        Assert.Equal("order-key", capturedContext.TryGetPayload<KafkaTransportContext<string>>()!.Key);
        Assert.Equal(new KeyValuePair<string, string>("reason", "deleted"), capturedContext.Headers[0]);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextHasKey()
    {
        // Arrange
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);
        var message = new EventMessage<string, string>("my-key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        var messageKey = capturedContext.TryGetPayload<KafkaTransportContext<string>>();
        Assert.NotNull(messageKey);
        Assert.Equal("my-key", messageKey.Key);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextHasSourceAndDestinationAddress()
    {
        // Arrange
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);
        var message = new EventMessage<string, string>("key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(TestDestination, capturedContext.DestinationAddress);
        Assert.Equal(TestHost, capturedContext.SourceAddress);
        Assert.Equal("kafka", EmitEndpointAddress.GetScheme(capturedContext.DestinationAddress));
    }

    [Fact]
    public async Task GivenMessageWithForwardedHeaders_WhenProduceAsync_ThenItsOwnIdAndSourceAddressReplaceTheForwardedOnes()
    {
        // Arrange
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, timeProvider);
        var message = new EventMessage<string, string>(
            "key",
            "value",
            [
                new("correlation-id", "test-correlation"),
                new(WellKnownHeaders.MessageId, "consumed-message-id"),
                new(WellKnownHeaders.SourceAddress, "kafka://other-broker:9092"),
                new("source", "test-source")
            ]);

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(
            [
                new("correlation-id", "test-correlation"),
                new("source", "test-source"),
                new(WellKnownHeaders.MessageId, capturedContext.MessageId),
                new(WellKnownHeaders.SourceAddress, TestHost.ToString()),
            ],
            capturedContext.Headers);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextTimestampFromTimeProvider()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(fixedTime);
        SendContext<string>? capturedContext = null;
        IMiddlewarePipeline<SendContext<string>> pipeline = new TestPipeline<SendContext<string>>(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });
        var producer = new KafkaPipelineProducer<string, string>(pipeline, TestDestination, TestHost, null!, tp);
        var message = new EventMessage<string, string>("key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(fixedTime, capturedContext.Timestamp);
    }
}
