namespace Emit.UnitTests.Observability;

using global::Emit.Observability;
using global::Emit.UnitTests.Coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public class ObserverNotificationQueueTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    private readonly FakeTimeProvider time = new();
    private readonly ObserverNotificationQueue queue = new(InlineTaskScheduler.Instance, NullLogger.Instance);

    [Fact]
    public async Task GivenQueuedNotifications_WhenFlushed_ThenItCompletesOnceTheyRanAndAtOnceWhenNoneIsQueued()
    {
        // Arrange: a node shutting down waits for its observers to hear what happened before the host disposes them
        var firstReturned = new TaskCompletionSource();
        List<string> notified = [];
        queue.Post(() =>
        {
            notified.Add("first");
            return firstReturned.Task;
        });
        queue.Post(() =>
        {
            notified.Add("second");
            return Task.CompletedTask;
        });

        // Act
        var flushing = queue.FlushAsync(Bound, time);

        // Assert
        Assert.False(flushing.IsCompleted);

        // Act
        await Task.Run(firstReturned.SetResult);

        // Assert
        await flushing.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["first", "second"], notified);
        Assert.True(queue.FlushAsync(Bound, time).IsCompleted);
    }

    [Fact]
    public async Task GivenObserverThatDoesNotReturn_WhenFlushed_ThenItCompletesAtTheBoundEvenAsMoreNotificationsArePosted()
    {
        // Arrange: a slow observer must not hold up shutdown past its bound
        queue.Post(() => new TaskCompletionSource().Task);
        var flushing = queue.FlushAsync(Bound, time);

        // Act
        queue.Post(() => Task.CompletedTask);
        time.Advance(Bound - TimeSpan.FromTicks(1));

        // Assert
        Assert.False(flushing.IsCompleted);

        // Act
        time.Advance(TimeSpan.FromTicks(1));

        // Assert
        await flushing.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
