namespace Emit.UnitTests.DependencyInjection;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using global::Emit.DependencyInjection;
using Xunit;

public sealed class ErrorActionBuilderTests
{
    [Fact]
    public void GivenDeadLetterWithoutTopic_WhenCalled_ThenStoresDeadLetterActionWithNullTopic()
    {
        // Arrange
        var builder = new ErrorActionBuilder();

        // Act
        builder.DeadLetter();

        // Assert
        var action = builder.Build();
        var deadLetter = Assert.IsType<ErrorAction.DeadLetterAction>(action);
        Assert.Null(deadLetter.TopicName);
    }

    [Fact]
    public void GivenDeadLetterWithTopic_WhenCalled_ThenStoresDeadLetterActionWithSpecifiedTopic()
    {
        // Arrange
        var builder = new ErrorActionBuilder();

        // Act
        builder.DeadLetter("validation-dlt");

        // Assert
        var action = builder.Build();
        var deadLetter = Assert.IsType<ErrorAction.DeadLetterAction>(action);
        Assert.Equal("validation-dlt", deadLetter.TopicName);
    }

    [Fact]
    public void GivenDiscard_WhenCalled_ThenStoresDiscardAction()
    {
        // Arrange
        var builder = new ErrorActionBuilder();

        // Act
        builder.Discard();

        // Assert
        var action = builder.Build();
        Assert.IsType<ErrorAction.DiscardAction>(action);
    }

    [Fact]
    public void GivenDeadLetterAlreadyCalled_WhenDiscardCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorActionBuilder();
        builder.DeadLetter();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Discard());
        Assert.Contains("already been configured", ex.Message);
    }

    [Fact]
    public void GivenDiscardAlreadyCalled_WhenDeadLetterCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorActionBuilder();
        builder.Discard();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.DeadLetter());
        Assert.Contains("already been configured", ex.Message);
    }

    [Fact]
    public void GivenNoActionConfigured_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorActionBuilder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("An error action is required", ex.Message);
    }

}
