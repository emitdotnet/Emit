namespace Emit.Tests.ErrorHandling;

using global::Emit.Abstractions;
using global::Emit.Abstractions.ErrorHandling;
using Xunit;

public sealed class ErrorPolicyBuilderTests
{
    [Fact]
    public void GivenExactTypeMatch_WhenEvaluate_ThenReturnsConfiguredAction()
    {
        // Arrange
        var policy = new ErrorPolicyBuilder()
            .When<InvalidOperationException>(a => a.Retry(3, Backoff.None).Discard())
            .Default(a => a.Discard())
            .Build();

        // Act
        var result = policy.Evaluate(new InvalidOperationException());

        // Assert
        Assert.IsType<ErrorAction.RetryAction>(result);
    }

    [Fact]
    public void GivenInheritanceMatch_WhenFileNotFoundException_ThenMatchesIOException()
    {
        // Arrange
        var policy = new ErrorPolicyBuilder()
            .When<IOException>(a => a.Retry(3, Backoff.None).Discard())
            .Default(a => a.Discard())
            .Build();

        // Act
        var result = policy.Evaluate(new FileNotFoundException());

        // Assert
        Assert.IsType<ErrorAction.RetryAction>(result);
    }

    [Fact]
    public void GivenPredicateFilter_WhenPredicateTrue_ThenReturnsAction()
    {
        // Arrange
        var policy = new ErrorPolicyBuilder()
            .When<HttpRequestException>(
                ex => ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
                a => a.Retry(3, Backoff.None).Discard())
            .Default(a => a.DeadLetter())
            .Build();

        // Act
        var result = policy.Evaluate(
            new HttpRequestException(null, null, System.Net.HttpStatusCode.ServiceUnavailable));

        // Assert
        Assert.IsType<ErrorAction.RetryAction>(result);
    }

    [Fact]
    public void GivenPredicateNonMatch_WhenEvaluate_ThenFallsThrough()
    {
        // Arrange
        var policy = new ErrorPolicyBuilder()
            .When<HttpRequestException>(
                ex => ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
                a => a.Retry(3, Backoff.None).Discard())
            .Default(a => a.DeadLetter())
            .Build();

        // Act — predicate won't match because status code is NotFound, not ServiceUnavailable
        var result = policy.Evaluate(
            new HttpRequestException(null, null, System.Net.HttpStatusCode.NotFound));

        // Assert
        Assert.IsType<ErrorAction.DeadLetterAction>(result);
    }

    [Fact]
    public void GivenDefaultOnly_WhenAnyException_ThenReturnsDefault()
    {
        // Arrange
        var policy = new ErrorPolicyBuilder()
            .Default(a => a.DeadLetter())
            .Build();

        // Act
        var result = policy.Evaluate(new InvalidOperationException());

        // Assert
        Assert.IsType<ErrorAction.DeadLetterAction>(result);
    }

    [Fact]
    public void GivenMultipleWhen_WhenEvaluate_ThenRespectsRegistrationOrder()
    {
        // Arrange
        var policy = new ErrorPolicyBuilder()
            .When<Exception>(a => a.Retry(1, Backoff.None).Discard())
            .When<InvalidOperationException>(a => a.Retry(5, Backoff.None).Discard())
            .Default(a => a.Discard())
            .Build();

        // Act — Exception matches first because it was registered first
        var result = policy.Evaluate(new InvalidOperationException());

        // Assert
        var retry = Assert.IsType<ErrorAction.RetryAction>(result);
        Assert.Equal(1, retry.MaxAttempts);
    }

    [Fact]
    public void GivenNoDefault_WhenBuild_ThenThrows()
    {
        // Arrange
        var builder = new ErrorPolicyBuilder()
            .When<InvalidOperationException>(a => a.Discard());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void GivenMultipleDefaultCalls_WhenBuild_ThenThrows()
    {
        // Arrange
        var builder = new ErrorPolicyBuilder()
            .Default(a => a.Discard());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Default(a => a.DeadLetter()));
    }

}
