namespace Emit.EntityFrameworkCore.Tests;

using Emit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

public class EfCoreTransactionContextTests
{
    [Fact]
    public async Task GivenNewTransaction_WhenCommitAsync_ThenIsCommittedIsTrue()
    {
        // Arrange
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);

        // Act
        await context.CommitAsync();

        // Assert
        Assert.True(context.IsCommitted);
        Assert.False(context.IsRolledBack);
        mockContextTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenCommitAgain_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);
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
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);

        // Act
        await context.RollbackAsync();

        // Assert
        Assert.False(context.IsCommitted);
        Assert.True(context.IsRolledBack);
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenRolledBackTransaction_WhenRollbackAgain_ThenNoException()
    {
        // Arrange
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);
        await context.RollbackAsync();

        // Act & Assert (should not throw)
        await context.RollbackAsync();

        Assert.True(context.IsRolledBack);
        // Should only rollback once (second call returns early)
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenRollbackAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);
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
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);
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
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.True(context.IsRolledBack);
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockContextTransaction.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenDisposeAsync_ThenNoRollback()
    {
        // Arrange
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var context = new EfCoreTransactionContext(mockContextTransaction.Object);
        await context.CommitAsync();

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.True(context.IsCommitted);
        Assert.False(context.IsRolledBack);
        // Should not rollback since already committed
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockContextTransaction.Verify(t => t.DisposeAsync(), Times.Once);
    }
}
