namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;

public class KafkaProducerBuilderTests
{
    [Fact]
    public void GivenNewProducerBuilder_WhenCreated_ThenOutboxEnabledIsFalse()
    {
        // Arrange & Act
        var builder = new KafkaProducerBuilder<string, string>();

        // Assert
        Assert.False(builder.OutboxEnabled);
    }

    [Fact]
    public void GivenUseOutbox_WhenCalled_ThenOutboxEnabledIsTrue()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();

        // Act
        builder.UseOutbox();

        // Assert
        Assert.True(builder.OutboxEnabled);
    }

    [Fact]
    public void GivenUseOutbox_WhenCalled_ThenReturnsSameBuilderForChaining()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();

        // Act
        var result = builder.UseOutbox();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenUseOutboxCalledTwice_WhenCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();
        builder.UseOutbox();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.UseOutbox());
        Assert.Contains(nameof(KafkaProducerBuilder<string, string>.UseOutbox), exception.Message);
    }
}
