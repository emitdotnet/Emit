namespace Emit.Abstractions.Tests.ErrorHandling;

using global::Emit.Abstractions.ErrorHandling;
using Xunit;

public sealed class ErrorPolicyActionBuilderTests
{
    [Fact]
    public void GivenRetryWithDiscard_WhenBuild_ThenReturnsRetryActionWithDiscardExhaustion()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act
        builder.Retry(3, Backoff.None).Discard();
        var action = builder.Build();

        // Assert
        var retry = Assert.IsType<ErrorAction.RetryAction>(action);
        Assert.Equal(3, retry.MaxAttempts);
        Assert.Same(Backoff.None, retry.Backoff);
        Assert.IsType<ErrorAction.DiscardAction>(retry.ExhaustionAction);
    }

    [Fact]
    public void GivenRetryWithDeadLetter_WhenBuild_ThenReturnsRetryActionWithDeadLetterExhaustion()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act
        builder.Retry(3, Backoff.None).DeadLetter("my-dlt");
        var action = builder.Build();

        // Assert
        var retry = Assert.IsType<ErrorAction.RetryAction>(action);
        Assert.Equal(3, retry.MaxAttempts);
        var exhaustion = Assert.IsType<ErrorAction.DeadLetterAction>(retry.ExhaustionAction);
        Assert.Equal("my-dlt", exhaustion.TopicName);
    }

    [Fact]
    public void GivenDiscard_WhenBuild_ThenReturnsDiscardAction()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act
        builder.Discard();
        var action = builder.Build();

        // Assert
        Assert.IsType<ErrorAction.DiscardAction>(action);
    }

    [Fact]
    public void GivenDeadLetter_WhenBuild_ThenReturnsDeadLetterAction()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act
        builder.DeadLetter("orders-dlt");
        var action = builder.Build();

        // Assert
        var deadLetter = Assert.IsType<ErrorAction.DeadLetterAction>(action);
        Assert.Equal("orders-dlt", deadLetter.TopicName);
    }

    [Fact]
    public void GivenRetryWithZeroAttempts_WhenCalled_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Retry(0, Backoff.None));
    }

    [Fact]
    public void GivenRetryWithNullBackoff_WhenCalled_ThenThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.Retry(3, null!));
    }

    [Fact]
    public void GivenRetryWithoutTerminal_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();
        builder.Retry(3, Backoff.None);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Retry must be followed by an exhaustion action", ex.Message);
    }

    [Fact]
    public void GivenDiscardThenRetry_WhenCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();
        builder.Discard();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Retry(3, Backoff.None));
        Assert.Contains("already been configured", ex.Message);
    }

    [Fact]
    public void GivenDeadLetterThenDiscard_WhenCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();
        builder.DeadLetter("my-dlt");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Discard());
        Assert.Contains("already been configured", ex.Message);
    }

    [Fact]
    public void GivenNoActionConfigured_WhenBuild_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ErrorPolicyActionBuilder();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("An error action is required", ex.Message);
    }
}
