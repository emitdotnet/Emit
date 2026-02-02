namespace Emit.Tests.Models;

using Emit.Models;
using MessagePack;
using Xunit;

public class OutboxAttemptTests
{
    [Fact]
    public void GivenValidParameters_WhenCreatingAttempt_ThenAllPropertiesAreSet()
    {
        // Arrange
        var attemptedAt = DateTime.UtcNow;
        var reason = "BrokerUnreachable";
        var message = "Connection refused";
        var exceptionType = "System.Net.Sockets.SocketException";

        // Act
        var attempt = new OutboxAttempt(attemptedAt, reason, message, exceptionType);

        // Assert
        Assert.Equal(attemptedAt, attempt.AttemptedAt);
        Assert.Equal(reason, attempt.Reason);
        Assert.Equal(message, attempt.Message);
        Assert.Equal(exceptionType, attempt.ExceptionType);
    }

    [Fact]
    public void GivenException_WhenCreatingFromException_ThenAttemptIsCreatedWithUtcTime()
    {
        // Arrange
        var reason = "TestError";
        var exception = new InvalidOperationException("Test error message");
        var beforeCreate = DateTime.UtcNow;

        // Act
        var attempt = OutboxAttempt.FromException(reason, exception);
        var afterCreate = DateTime.UtcNow;

        // Assert
        Assert.Equal(reason, attempt.Reason);
        Assert.Equal(exception.Message, attempt.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, attempt.ExceptionType);
        Assert.True(attempt.AttemptedAt >= beforeCreate && attempt.AttemptedAt <= afterCreate);
        Assert.Equal(DateTimeKind.Utc, attempt.AttemptedAt.Kind);
    }

    [Fact]
    public void GivenNullException_WhenCreatingFromException_ThenThrowsArgumentNullException()
    {
        // Arrange
        var reason = "TestError";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => OutboxAttempt.FromException(reason, null!));
    }

    [Fact]
    public void GivenNullReason_WhenCreatingFromException_ThenThrowsArgumentNullException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => OutboxAttempt.FromException(null!, exception));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenEmptyOrWhitespaceReason_WhenCreatingFromException_ThenThrowsArgumentException(string reason)
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => OutboxAttempt.FromException(reason, exception));
    }

    [Fact]
    public void GivenOutboxAttempt_WhenSerializingWithMessagePack_ThenCanDeserialize()
    {
        // Arrange
        var original = new OutboxAttempt(
            DateTime.UtcNow,
            "BrokerUnreachable",
            "Connection refused",
            "System.Net.Sockets.SocketException");

        // Act
        var bytes = MessagePackSerializer.Serialize(original);
        var deserialized = MessagePackSerializer.Deserialize<OutboxAttempt>(bytes);

        // Assert
        Assert.Equal(original.AttemptedAt, deserialized.AttemptedAt);
        Assert.Equal(original.Reason, deserialized.Reason);
        Assert.Equal(original.Message, deserialized.Message);
        Assert.Equal(original.ExceptionType, deserialized.ExceptionType);
    }
}
