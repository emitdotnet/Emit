namespace Emit.UnitTests.Observability;

using global::Emit.Abstractions.Observability;
using global::Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class LeaderElectionObserverInvokerTests
{
    private static readonly Guid TestNodeId = Guid.NewGuid();

    [Fact]
    public async Task GivenNoObservers_WhenOnLeaderElectedAsync_ThenNoException()
    {
        // Arrange
        var invoker = CreateInvoker([]);

        // Act
        await invoker.OnLeaderElectedAsync(TestNodeId, CancellationToken.None);

        // Assert - no exception thrown
    }

    [Fact]
    public async Task GivenObserver_WhenOnLeaderElectedAsync_ThenObserverCalled()
    {
        // Arrange
        var mock = new Mock<ILeaderElectionObserver>();
        var invoker = CreateInvoker([mock.Object]);

        // Act
        await invoker.OnLeaderElectedAsync(TestNodeId, CancellationToken.None);

        // Assert
        mock.Verify(o => o.OnLeaderElectedAsync(TestNodeId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GivenMultipleObservers_WhenOnLeaderLostAsync_ThenAllObserversCalled()
    {
        // Arrange
        var mock1 = new Mock<ILeaderElectionObserver>();
        var mock2 = new Mock<ILeaderElectionObserver>();
        var invoker = CreateInvoker([mock1.Object, mock2.Object]);

        // Act
        await invoker.OnLeaderLostAsync(TestNodeId, CancellationToken.None);

        // Assert
        mock1.Verify(o => o.OnLeaderLostAsync(TestNodeId, CancellationToken.None), Times.Once);
        mock2.Verify(o => o.OnLeaderLostAsync(TestNodeId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GivenObserverThrows_WhenOnNodeRegisteredAsync_ThenOtherObserversStillCalled()
    {
        // Arrange
        var mock1 = new Mock<ILeaderElectionObserver>();
        var mock2 = new Mock<ILeaderElectionObserver>();

        mock1.Setup(o => o.OnNodeRegisteredAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Observer1 failed"));

        var invoker = CreateInvoker([mock1.Object, mock2.Object]);

        // Act
        await invoker.OnNodeRegisteredAsync(TestNodeId, CancellationToken.None);

        // Assert
        mock2.Verify(o => o.OnNodeRegisteredAsync(TestNodeId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GivenObserver_WhenOnNodeRemovedAsync_ThenObserverCalled()
    {
        // Arrange
        var mock = new Mock<ILeaderElectionObserver>();
        var invoker = CreateInvoker([mock.Object]);

        // Act
        await invoker.OnNodeRemovedAsync(TestNodeId, CancellationToken.None);

        // Assert
        mock.Verify(o => o.OnNodeRemovedAsync(TestNodeId, CancellationToken.None), Times.Once);
    }

    private static LeaderElectionObserverInvoker CreateInvoker(ILeaderElectionObserver[] observers)
    {
        return new LeaderElectionObserverInvoker(
            observers,
            NullLogger<LeaderElectionObserverInvoker>.Instance);
    }
}
