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
        var action = ErrorAction.Retry(3, Backoff.None, ErrorAction.DeadLetter());

        // Assert
        var retry = Assert.IsType<ErrorAction.RetryAction>(action);
        Assert.IsType<ErrorAction.DeadLetterAction>(retry.ExhaustionAction);
    }

    [Fact]
    public void GivenDeadLetter_WhenCreated_ThenReturnsDeadLetterAction()
    {
        // Act
        var action = ErrorAction.DeadLetter();

        // Assert
        Assert.IsType<ErrorAction.DeadLetterAction>(action);
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
