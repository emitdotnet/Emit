namespace Emit.Abstractions.Tests.ErrorHandling;

using global::Emit.Abstractions.ErrorHandling;
using Xunit;

public sealed class ErrorActionTests
{
    [Fact]
    public void GivenRetry_WhenValidParameters_ThenCreatesRetryAction()
    {
        // Act
        var action = ErrorAction.Retry(3, Backoff.None, ErrorAction.Discard());

        // Assert
        var retry = Assert.IsType<ErrorAction.RetryAction>(action);
        Assert.Equal(3, retry.MaxAttempts);
        Assert.Same(Backoff.None, retry.Backoff);
        Assert.IsType<ErrorAction.DiscardAction>(retry.ExhaustionAction);
    }

    [Fact]
    public void GivenRetry_WhenZeroAttempts_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ErrorAction.Retry(0, Backoff.None, ErrorAction.Discard()));
    }

    [Fact]
    public void GivenRetry_WhenNullBackoff_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => ErrorAction.Retry(3, null!, ErrorAction.Discard()));
    }

    [Fact]
    public void GivenRetry_WhenNullExhaustionAction_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => ErrorAction.Retry(3, Backoff.None, null!));
    }

    [Fact]
    public void GivenRetry_WhenExhaustionActionProvided_ThenStoresExhaustionAction()
    {
        // Act
        var action = ErrorAction.Retry(3, Backoff.None, ErrorAction.DeadLetter("my-dlt"));

        // Assert
        var retry = Assert.IsType<ErrorAction.RetryAction>(action);
        var exhaustion = Assert.IsType<ErrorAction.DeadLetterAction>(retry.ExhaustionAction);
        Assert.Equal("my-dlt", exhaustion.TopicName);
    }

    [Fact]
    public void GivenDeadLetterWithoutTopic_WhenCreated_ThenTopicNameIsNull()
    {
        // Act
        var action = ErrorAction.DeadLetter();

        // Assert
        var dl = Assert.IsType<ErrorAction.DeadLetterAction>(action);
        Assert.Null(dl.TopicName);
    }

    [Fact]
    public void GivenDeadLetterWithTopic_WhenCreated_ThenTopicNameIsSet()
    {
        // Act
        var action = ErrorAction.DeadLetter("my-dlq");

        // Assert
        var dl = Assert.IsType<ErrorAction.DeadLetterAction>(action);
        Assert.Equal("my-dlq", dl.TopicName);
    }

    [Fact]
    public void GivenDeadLetterWithEmptyTopic_WhenCreated_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => ErrorAction.DeadLetter(""));
    }

    [Fact]
    public void GivenDiscard_WhenCreated_ThenReturnsDiscardAction()
    {
        // Act
        var action = ErrorAction.Discard();

        // Assert
        Assert.IsType<ErrorAction.DiscardAction>(action);
    }

}
