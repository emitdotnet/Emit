namespace Emit.Kafka.Tests;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Kafka;
using global::Emit.Kafka.Consumer;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public sealed class KafkaPipelineProducerTests
{
    private readonly FakeTimeProvider timeProvider = new(DateTimeOffset.UtcNow);

    [Fact]
    public async Task GivenNullMessage_WhenProduceAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        MessageDelegate<OutboundContext<string>> pipeline = _ => Task.CompletedTask;
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, timeProvider);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => producer.ProduceAsync(null!));
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenPipelineInvoked()
    {
        // Arrange
        var pipelineInvoked = false;
        MessageDelegate<OutboundContext<string>> pipeline = _ =>
        {
            pipelineInvoked = true;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, timeProvider);
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
        OutboundContext<string>? capturedContext = null;
        MessageDelegate<OutboundContext<string>> pipeline = ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, timeProvider);
        var message = new EventMessage<string, string>("key", "test-value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal("test-value", capturedContext.Message);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenOutboundKafkaContextHasKeyAndTopic()
    {
        // Arrange
        OutboundKafkaContext<string, string>? capturedContext = null;
        MessageDelegate<OutboundContext<string>> pipeline = ctx =>
        {
            capturedContext = (OutboundKafkaContext<string, string>)ctx;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "orders", null!, timeProvider);
        var message = new EventMessage<string, string>("order-key", "order-value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal("order-key", capturedContext.Key);
        Assert.Equal("orders", capturedContext.Topic);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextHasKeyFeature()
    {
        // Arrange
        OutboundContext<string>? capturedContext = null;
        MessageDelegate<OutboundContext<string>> pipeline = ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, timeProvider);
        var message = new EventMessage<string, string>("my-key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        var keyFeature = capturedContext.Features.Get<IKeyFeature<string>>();
        Assert.NotNull(keyFeature);
        Assert.Equal("my-key", keyFeature.Key);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextHasKafkaFeature()
    {
        // Arrange
        OutboundContext<string>? capturedContext = null;
        MessageDelegate<OutboundContext<string>> pipeline = ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, timeProvider);
        var message = new EventMessage<string, string>("key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        var kafkaFeature = capturedContext.Features.Get<IKafkaFeature>();
        Assert.NotNull(kafkaFeature);
        Assert.Equal("test-topic", kafkaFeature.Topic);
    }

    [Fact]
    public async Task GivenMessageWithHeaders_WhenProduceAsync_ThenContextHasHeadersFeature()
    {
        // Arrange
        OutboundContext<string>? capturedContext = null;
        MessageDelegate<OutboundContext<string>> pipeline = ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, timeProvider);
        var message = new EventMessage<string, string>(
            "key",
            "value",
            [
                new("correlation-id", "test-correlation"),
                new("source", "test-source")
            ]);

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        var headersFeature = capturedContext.Features.Get<IHeadersFeature>();
        Assert.NotNull(headersFeature);
        Assert.Equal(2, headersFeature.Headers.Count);
    }

    [Fact]
    public async Task GivenValidMessage_WhenProduceAsync_ThenContextTimestampFromTimeProvider()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(fixedTime);
        OutboundContext<string>? capturedContext = null;
        MessageDelegate<OutboundContext<string>> pipeline = ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        };
        var producer = new KafkaPipelineProducer<string, string>(pipeline, "test-topic", null!, tp);
        var message = new EventMessage<string, string>("key", "value");

        // Act
        await producer.ProduceAsync(message);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(fixedTime, capturedContext.Timestamp);
    }
}
