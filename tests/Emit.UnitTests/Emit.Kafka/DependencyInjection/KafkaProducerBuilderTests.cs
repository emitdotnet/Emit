namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Kafka.DependencyInjection;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class KafkaProducerBuilderTests
{
    [Fact]
    public void GivenProducerBuilder_WhenNewlyCreated_ThenDirectEnabledFalse()
    {
        // Arrange & Act
        var builder = new KafkaProducerBuilder<string, string>();

        // Assert
        Assert.False(builder.DirectEnabled);
    }

    [Fact]
    public void GivenProducerBuilder_WhenUseDirectCalled_ThenDirectEnabledTrue()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();

        // Act
        builder.UseDirect();

        // Assert
        Assert.True(builder.DirectEnabled);
    }

    [Fact]
    public void GivenUseDirect_WhenCalled_ThenReturnsSameBuilderForChaining()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();

        // Act
        var result = builder.UseDirect();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public async Task GivenScopedMiddlewareFactory_WhenTwoMessagesAreProduced_ThenItCreatesAMiddlewarePerMessage()
    {
        // Arrange
        var created = 0;
        var builder = new KafkaProducerBuilder<string, string>();
        builder.Use(
            _ =>
            {
                created++;
                return new PassThroughMiddleware();
            },
            MiddlewareLifetime.Scoped);
        var provider = new ServiceCollection().BuildServiceProvider();
        var pipeline = MessagePipelineBuilder.Build<SendContext<string>, string>(
            provider, new TestPipeline<SendContext<string>>(_ => Task.CompletedTask), builder.Pipeline);

        // Act
        await pipeline.InvokeAsync(Context(provider));
        await pipeline.InvokeAsync(Context(provider));

        // Assert
        Assert.Equal(2, created);
    }

    [Fact]
    public void GivenProducerBuilder_WhenUseDirectCalledTwice_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();
        builder.UseDirect();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.UseDirect());
        Assert.Contains(nameof(KafkaProducerBuilder<string, string>.UseDirect), exception.Message);
    }

    private static SendContext<string> Context(IServiceProvider services) => new()
    {
        MessageId = "test",
        Timestamp = DateTimeOffset.UtcNow,
        CancellationToken = CancellationToken.None,
        Services = services,
        Message = "hello",
    };

    private sealed class PassThroughMiddleware : IMiddleware<SendContext>
    {
        public Task InvokeAsync(SendContext context, Func<Task> next) => next();
    }
}
