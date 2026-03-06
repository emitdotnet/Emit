namespace Emit.MongoDB.Tests;

using Emit.MongoDB;
using global::MongoDB.Driver;
using Moq;
using Xunit;

public class MongoTransactionContextTests
{
    [Fact]
    public async Task GivenNewTransaction_WhenCommitAsync_ThenIsCommittedIsTrue()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };

        // Act
        await context.CommitAsync();

        // Assert
        Assert.True(context.IsCommitted);
        Assert.False(context.IsRolledBack);
        mockSession.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenCommitAgain_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };
        await context.CommitAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.CommitAsync());

        Assert.Contains("already been committed", exception.Message);
    }

    [Fact]
    public async Task GivenNewTransaction_WhenRollbackAsync_ThenIsRolledBackIsTrue()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };

        // Act
        await context.RollbackAsync();

        // Assert
        Assert.False(context.IsCommitted);
        Assert.True(context.IsRolledBack);
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenRolledBackTransaction_WhenRollbackAgain_ThenNoException()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };
        await context.RollbackAsync();

        // Act & Assert (should not throw)
        await context.RollbackAsync();

        Assert.True(context.IsRolledBack);
        // Should only abort once (second call returns early)
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenRollbackAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };
        await context.CommitAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.RollbackAsync());

        Assert.Contains("Cannot rollback a committed transaction", exception.Message);
    }

    [Fact]
    public async Task GivenRolledBackTransaction_WhenCommitAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };
        await context.RollbackAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.CommitAsync());

        Assert.Contains("already been rolled back", exception.Message);
    }

    [Fact]
    public async Task GivenUncommittedTransaction_WhenDisposeAsync_ThenAutoRollback()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.True(context.IsRolledBack);
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockSession.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenDisposeAsync_ThenNoRollback()
    {
        // Arrange
        var mockSession = new Mock<IClientSessionHandle>();
        var context = new MongoTransactionContext { Session = mockSession.Object };
        await context.CommitAsync();

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.True(context.IsCommitted);
        Assert.False(context.IsRolledBack);
        // Should not abort since already committed
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockSession.Verify(s => s.Dispose(), Times.Once);
    }
}
