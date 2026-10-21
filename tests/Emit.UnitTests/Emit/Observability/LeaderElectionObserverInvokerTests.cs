namespace Emit.UnitTests.Observability;

using global::Emit.Abstractions.Observability;
using global::Emit.Observability;
using global::Emit.UnitTests.Coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class LeaderElectionObserverInvokerTests
{
    private static readonly Guid TestNodeId = Guid.NewGuid();

    [Fact]
    public async Task GivenObserverThatHasNotReturned_WhenMoreEventsAreRaised_ThenTheCallerDoesNotWaitAndTheyFollowInOrder()
    {
        // Arrange: notifications are raised on the heartbeat, which must not wait for observer code
        var returned = new TaskCompletionSource();
        List<string> calls = [];
        var observer = new Mock<ILeaderElectionObserver>();
        observer.Setup(o => o.OnLeaderElectedAsync(TestNodeId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("elected"))
            .Returns(returned.Task);
        observer.Setup(o => o.OnLeaderLostAsync(TestNodeId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("lost"))
            .Returns(Task.CompletedTask);
        var invoker = CreateInvoker(observer.Object);

        // Act
        invoker.OnLeaderElected(TestNodeId);
        invoker.OnLeaderLost(TestNodeId);

        // Assert
        Assert.Equal(["elected"], calls);

        // Act
        await ReturnAsync(returned);

        // Assert
        Assert.Equal(["elected", "lost"], calls);
    }

    [Fact]
    public void GivenObserverThatThrows_WhenEventsAreRaised_ThenTheOtherObserversAndLaterEventsAreStillNotified()
    {
        // Arrange
        var failing = new Mock<ILeaderElectionObserver>();
        failing.Setup(o => o.OnNodeRegisteredAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Observer failed"));
        var other = new Mock<ILeaderElectionObserver>();
        var invoker = CreateInvoker(failing.Object, other.Object);

        // Act
        invoker.OnNodeRegistered(TestNodeId);
        invoker.OnNodeRemoved(TestNodeId);

        // Assert
        other.Verify(o => o.OnNodeRegisteredAsync(TestNodeId, It.IsAny<CancellationToken>()), Times.Once);
        other.Verify(o => o.OnNodeRemovedAsync(TestNodeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenObserverThatNeverReturns_WhenEventsKeepBeingRaised_ThenOnlyABoundedBacklogIsKept()
    {
        // Arrange
        var returned = new TaskCompletionSource();
        var calls = 0;
        var observer = new Mock<ILeaderElectionObserver>();
        observer.Setup(o => o.OnLeaderElectedAsync(TestNodeId, It.IsAny<CancellationToken>()))
            .Callback(() => calls++)
            .Returns(() => calls == 1 ? returned.Task : Task.CompletedTask);
        var invoker = CreateInvoker(observer.Object);

        // Act
        for (var i = 0; i < ObserverNotificationQueue.MaxPending + 10; i++)
        {
            invoker.OnLeaderElected(TestNodeId);
        }

        await ReturnAsync(returned);

        // Assert: the notification in progress, and the backlog behind it
        Assert.Equal(ObserverNotificationQueue.MaxPending + 1, calls);
    }

    /// <summary>
    /// Completes an observer call away from the test's synchronization context, so the notifications behind it
    /// run before this returns.
    /// </summary>
    private static Task ReturnAsync(TaskCompletionSource observerCall) => Task.Run(observerCall.SetResult);

    private static LeaderElectionObserverInvoker CreateInvoker(params ILeaderElectionObserver[] observers) =>
        new(observers, InlineTaskScheduler.Instance, NullLogger<LeaderElectionObserverInvoker>.Instance);
}
