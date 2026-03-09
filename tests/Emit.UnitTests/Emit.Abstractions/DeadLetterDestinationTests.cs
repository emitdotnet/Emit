namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions.ErrorHandling;
using Xunit;

public sealed class DeadLetterActionResolveTests
{
    [Fact]
    public void GivenExplicitTopic_WhenResolve_ThenReturnsExplicitTopic()
    {
        // Arrange
        var deadLetter = new ErrorAction.DeadLetterAction("explicit-dlq");

        // Act
        var result = deadLetter.Resolve(sourceTopic: "source-topic", resolveConvention: null);

        // Assert
        Assert.Equal("explicit-dlq", result);
    }

    [Fact]
    public void GivenExplicitTopicAndConvention_WhenResolve_ThenExplicitTakesPriority()
    {
        // Arrange
        var deadLetter = new ErrorAction.DeadLetterAction("explicit-dlq");
        Func<string, string?> convention = topic => $"{topic}-dlq";

        // Act
        var result = deadLetter.Resolve(sourceTopic: "source-topic", resolveConvention: convention);

        // Assert
        Assert.Equal("explicit-dlq", result);
    }

    [Fact]
    public void GivenNoExplicitTopic_WhenConventionResolves_ThenReturnsConventionResult()
    {
        // Arrange
        var deadLetter = new ErrorAction.DeadLetterAction(topicName: null);
        Func<string, string?> convention = topic => $"{topic}-dlq";

        // Act
        var result = deadLetter.Resolve(sourceTopic: "source-topic", resolveConvention: convention);

        // Assert
        Assert.Equal("source-topic-dlq", result);
    }

    [Fact]
    public void GivenNoExplicitTopicAndNoConvention_WhenResolve_ThenReturnsNull()
    {
        // Arrange
        var deadLetter = new ErrorAction.DeadLetterAction(topicName: null);

        // Act
        var result = deadLetter.Resolve(sourceTopic: "source-topic", resolveConvention: null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenNoExplicitTopicAndNullSourceTopic_WhenResolve_ThenReturnsNull()
    {
        // Arrange
        var deadLetter = new ErrorAction.DeadLetterAction(topicName: null);
        Func<string, string?> convention = topic => $"{topic}-dlq";

        // Act
        var result = deadLetter.Resolve(sourceTopic: null, resolveConvention: convention);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenWhitespaceExplicitTopic_WhenResolve_ThenUsesConvention()
    {
        // Arrange
        var deadLetter = new ErrorAction.DeadLetterAction(topicName: "   ");
        Func<string, string?> convention = topic => $"{topic}-dlq";

        // Act
        var result = deadLetter.Resolve(sourceTopic: "source-topic", resolveConvention: convention);

        // Assert
        Assert.Equal("source-topic-dlq", result);
    }
}
