namespace Emit.Tests.Models;

using Emit.Models;
using Xunit;

public class OutboxEntryTests
{
    [Fact]
    public void GivenRequiredProperties_WhenCreatingEntry_ThenDefaultValuesAreSet()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow;

        // Act
        var entry = new OutboxEntry
        {
            ProviderId = "kafka",
            RegistrationKey = "__default__",
            GroupKey = "cluster:topic",
            Payload = [1, 2, 3]
        };
        var afterCreate = DateTime.UtcNow;

        // Assert
        Assert.Null(entry.Id);
        Assert.Equal("kafka", entry.ProviderId);
        Assert.Equal("__default__", entry.RegistrationKey);
        Assert.Equal("cluster:topic", entry.GroupKey);
        Assert.Equal(0L, entry.Sequence);
        Assert.Equal(OutboxStatus.Pending, entry.Status);
        Assert.True(entry.EnqueuedAt >= beforeCreate && entry.EnqueuedAt <= afterCreate);
        Assert.Equal(DateTimeKind.Utc, entry.EnqueuedAt.Kind);
        Assert.Null(entry.CompletedAt);
        Assert.Equal(0, entry.RetryCount);
        Assert.Null(entry.LastAttemptedAt);
        Assert.Null(entry.LatestError);
        Assert.Empty(entry.Attempts);
        Assert.Equal([1, 2, 3], entry.Payload);
        Assert.Empty(entry.Properties);
        Assert.Null(entry.CorrelationId);
    }

    [Fact]
    public void GivenPendingEntry_WhenMarkingCompleted_ThenStatusAndCompletedAtAreSet()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.LatestError = "Previous error";
        var beforeComplete = DateTime.UtcNow;

        // Act
        entry.MarkCompleted();
        var afterComplete = DateTime.UtcNow;

        // Assert
        Assert.Equal(OutboxStatus.Completed, entry.Status);
        Assert.NotNull(entry.CompletedAt);
        Assert.True(entry.CompletedAt >= beforeComplete && entry.CompletedAt <= afterComplete);
        Assert.Equal(DateTimeKind.Utc, entry.CompletedAt.Value.Kind);
        Assert.Null(entry.LatestError);
    }

    [Fact]
    public void GivenPendingEntry_WhenMarkingFailed_ThenStatusRetryCountAndAttemptsAreUpdated()
    {
        // Arrange
        var entry = CreateTestEntry();
        var exception = new InvalidOperationException("Test error");
        var reason = "ProcessingError";
        var beforeFail = DateTime.UtcNow;

        // Act
        entry.MarkFailed(reason, exception);
        var afterFail = DateTime.UtcNow;

        // Assert
        Assert.Equal(OutboxStatus.Failed, entry.Status);
        Assert.Equal(1, entry.RetryCount);
        Assert.NotNull(entry.LastAttemptedAt);
        Assert.True(entry.LastAttemptedAt >= beforeFail && entry.LastAttemptedAt <= afterFail);
        Assert.Equal(DateTimeKind.Utc, entry.LastAttemptedAt.Value.Kind);
        Assert.Equal(exception.Message, entry.LatestError);
        Assert.Single(entry.Attempts);
        Assert.Equal(reason, entry.Attempts[0].Reason);
        Assert.Equal(exception.Message, entry.Attempts[0].Message);
    }

    [Fact]
    public void GivenMultipleFailures_WhenMarkingFailedRepeatedly_ThenRetryCountIncrementsAndAttemptsAccumulate()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.MarkFailed("Error1", new InvalidOperationException("First error"));
        entry.MarkFailed("Error2", new ArgumentException("Second error"));
        entry.MarkFailed("Error3", new TimeoutException("Third error"));

        // Assert
        Assert.Equal(3, entry.RetryCount);
        Assert.Equal(3, entry.Attempts.Count);
        Assert.Equal("Error1", entry.Attempts[0].Reason);
        Assert.Equal("Error2", entry.Attempts[1].Reason);
        Assert.Equal("Error3", entry.Attempts[2].Reason);
        Assert.Equal("Third error", entry.LatestError);
    }

    [Fact]
    public void GivenMaxAttempts_WhenExceedingLimit_ThenOldestAttemptsAreRemoved()
    {
        // Arrange
        var entry = CreateTestEntry();
        var maxAttempts = 3;

        // Act
        entry.MarkFailed("Error1", new Exception("Error 1"), maxAttempts);
        entry.MarkFailed("Error2", new Exception("Error 2"), maxAttempts);
        entry.MarkFailed("Error3", new Exception("Error 3"), maxAttempts);
        entry.MarkFailed("Error4", new Exception("Error 4"), maxAttempts);
        entry.MarkFailed("Error5", new Exception("Error 5"), maxAttempts);

        // Assert
        Assert.Equal(maxAttempts, entry.Attempts.Count);
        Assert.Equal("Error3", entry.Attempts[0].Reason);
        Assert.Equal("Error4", entry.Attempts[1].Reason);
        Assert.Equal("Error5", entry.Attempts[2].Reason);
        Assert.Equal(5, entry.RetryCount);
    }

    [Fact]
    public void GivenAttempt_WhenAddingWithMaxAttempts_ThenCollectionIsCapped()
    {
        // Arrange
        var entry = CreateTestEntry();
        var attempt1 = new OutboxAttempt(DateTime.UtcNow, "R1", "M1", "E1");
        var attempt2 = new OutboxAttempt(DateTime.UtcNow, "R2", "M2", "E2");
        var attempt3 = new OutboxAttempt(DateTime.UtcNow, "R3", "M3", "E3");

        // Act
        entry.AddAttempt(attempt1, maxAttempts: 2);
        entry.AddAttempt(attempt2, maxAttempts: 2);
        entry.AddAttempt(attempt3, maxAttempts: 2);

        // Assert
        Assert.Equal(2, entry.Attempts.Count);
        Assert.Equal("R2", entry.Attempts[0].Reason);
        Assert.Equal("R3", entry.Attempts[1].Reason);
    }

    [Fact]
    public void GivenNullAttempt_WhenAdding_ThenThrowsArgumentNullException()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => entry.AddAttempt(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenInvalidMaxAttempts_WhenAdding_ThenThrowsArgumentOutOfRangeException(int maxAttempts)
    {
        // Arrange
        var entry = CreateTestEntry();
        var attempt = new OutboxAttempt(DateTime.UtcNow, "R", "M", "E");

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.AddAttempt(attempt, maxAttempts));
    }

    [Fact]
    public void GivenPersistedAttempts_WhenRestoring_ThenAttemptsAreReplaced()
    {
        // Arrange
        var entry = CreateTestEntry();
        entry.AddAttempt(new OutboxAttempt(DateTime.UtcNow, "Old", "Old", "Old"));

        var persistedAttempts = new[]
        {
            new OutboxAttempt(DateTime.UtcNow, "R1", "M1", "E1"),
            new OutboxAttempt(DateTime.UtcNow, "R2", "M2", "E2")
        };

        // Act
        entry.RestoreAttempts(persistedAttempts);

        // Assert
        Assert.Equal(2, entry.Attempts.Count);
        Assert.Equal("R1", entry.Attempts[0].Reason);
        Assert.Equal("R2", entry.Attempts[1].Reason);
    }

    [Fact]
    public void GivenNullException_WhenMarkingFailed_ThenThrowsArgumentNullException()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => entry.MarkFailed("Reason", null!));
    }

    [Fact]
    public void GivenNullReason_WhenMarkingFailed_ThenThrowsArgumentNullException()
    {
        // Arrange
        var entry = CreateTestEntry();
        var exception = new Exception("Test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => entry.MarkFailed(null!, exception));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenEmptyOrWhitespaceReason_WhenMarkingFailed_ThenThrowsArgumentException(string reason)
    {
        // Arrange
        var entry = CreateTestEntry();
        var exception = new Exception("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => entry.MarkFailed(reason, exception));
    }

    [Fact]
    public void GivenProperties_WhenSettingValues_ThenCanRetrieve()
    {
        // Arrange
        var entry = CreateTestEntry();

        // Act
        entry.Properties["topic"] = "orders";
        entry.Properties["cluster"] = "production";
        entry.Properties["valueType"] = "OrderCreated";

        // Assert
        Assert.Equal(3, entry.Properties.Count);
        Assert.Equal("orders", entry.Properties["topic"]);
        Assert.Equal("production", entry.Properties["cluster"]);
        Assert.Equal("OrderCreated", entry.Properties["valueType"]);
    }

    private static OutboxEntry CreateTestEntry() => new()
    {
        ProviderId = "kafka",
        RegistrationKey = "__default__",
        GroupKey = "cluster:topic",
        Payload = [1, 2, 3]
    };
}
