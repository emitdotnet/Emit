namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
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
    public void GivenProducerBuilder_WhenUseDirectCalledTwice_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new KafkaProducerBuilder<string, string>();
        builder.UseDirect();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.UseDirect());
        Assert.Contains(nameof(KafkaProducerBuilder<string, string>.UseDirect), exception.Message);
    }
}
