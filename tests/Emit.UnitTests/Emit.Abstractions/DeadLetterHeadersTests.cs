namespace Emit.Abstractions.Tests;

using global::Emit.Abstractions;
using Xunit;

public sealed class DeadLetterHeadersTests
{
    [Fact]
    public void GivenOriginalHeaders_WhenCreateBase_ThenPreservesOriginalHeaders()
    {
        // Arrange
        var originalHeaders = new List<KeyValuePair<string, string>>
        {
            new("x-custom-header", "custom-value"),
            new("content-type", "application/json"),
        };

        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders,
            typeof(InvalidOperationException),
            "error");

        // Assert
        Assert.Contains(headers, h => h.Key == "x-custom-header" && h.Value == "custom-value");
        Assert.Contains(headers, h => h.Key == "content-type" && h.Value == "application/json");
    }

    [Fact]
    public void GivenNullOriginalHeaders_WhenCreateBase_ThenNoOriginalHeaders()
    {
        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders: null,
            typeof(InvalidOperationException),
            "error");

        // Assert
        Assert.DoesNotContain(headers, h => h.Key == "x-custom-header");
    }

    [Fact]
    public void GivenOriginalHeadersWithTraceparent_WhenCreateBase_ThenAddsOriginalTraceparentHeader()
    {
        // Arrange
        var traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var originalHeaders = new List<KeyValuePair<string, string>>
        {
            new("traceparent", traceparent),
        };

        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders,
            typeof(InvalidOperationException),
            "error");

        // Assert
        Assert.Contains(headers, h => h.Key == DeadLetterHeaders.OriginalTraceParent && h.Value == traceparent);
    }

    [Fact]
    public void GivenOriginalHeadersWithoutTraceparent_WhenCreateBase_ThenNoOriginalTraceparentHeader()
    {
        // Arrange
        var originalHeaders = new List<KeyValuePair<string, string>>
        {
            new("x-other-header", "value"),
        };

        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders,
            typeof(InvalidOperationException),
            "error");

        // Assert
        Assert.DoesNotContain(headers, h => h.Key == DeadLetterHeaders.OriginalTraceParent);
    }

    [Fact]
    public void GivenSourceTopicHeaders_WhenCreateBase_ThenTheLastIsTheOriginalTopic()
    {
        // Arrange: a dead letter replayed and consumed again carries the topic it was first consumed from too
        var originalHeaders = new List<KeyValuePair<string, string>>
        {
            new(DeadLetterHeaders.SourceTopic, "first-topic"),
            new(DeadLetterHeaders.SourceTopic, "my-source-topic"),
        };

        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders,
            typeof(InvalidOperationException),
            "error");

        // Assert
        Assert.Equal("my-source-topic", Assert.Single(headers, h => h.Key == DeadLetterHeaders.OriginalTopic).Value);
    }

    [Fact]
    public void GivenADeadLetterDeadLetteredAgain_WhenCreateBase_ThenItHasOnlyTheNewException()
    {
        // Arrange
        var originalHeaders = new List<KeyValuePair<string, string>>
        {
            new(DeadLetterHeaders.ExceptionType, typeof(TimeoutException).FullName!),
            new(DeadLetterHeaders.ExceptionMessage, "first failure"),
        };

        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders,
            typeof(InvalidOperationException),
            "error message");

        // Assert
        Assert.Equal(typeof(InvalidOperationException).FullName, Assert.Single(headers, h => h.Key == DeadLetterHeaders.ExceptionType).Value);
        Assert.Equal("error message", Assert.Single(headers, h => h.Key == DeadLetterHeaders.ExceptionMessage).Value);
    }

    [Fact]
    public void GivenAnyInput_WhenCreateBase_ThenAddsTimestampHeader()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders: null,
            typeof(InvalidOperationException),
            "error");

        // Assert
        var timestampHeader = Assert.Single(headers, h => h.Key == DeadLetterHeaders.Timestamp);
        var parsed = DateTimeOffset.Parse(timestampHeader.Value, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(parsed >= before);
        Assert.True(parsed <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void GivenCreateBase_WhenReturned_ThenListIsMutable()
    {
        // Act
        var headers = DeadLetterHeaders.CreateBase(
            originalHeaders: null,
            typeof(InvalidOperationException),
            "error");

        // Assert — can add to the returned list without exception
        headers.Add(new(DeadLetterHeaders.RetryCount, "3"));
        Assert.Contains(headers, h => h.Key == DeadLetterHeaders.RetryCount && h.Value == "3");
    }
}
