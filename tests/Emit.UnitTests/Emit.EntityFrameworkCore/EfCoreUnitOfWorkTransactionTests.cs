namespace Emit.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

public class EfCoreUnitOfWorkTransactionTests
{
    [Fact]
    public async Task GivenNewTransaction_WhenCommitAsyncCalled_ThenIsCommittedTrue()
    {
        // Arrange
        var (transaction, _, _) = CreateTransaction();

        // Act
        await transaction.CommitAsync();

        // Assert
        Assert.True(transaction.IsCommitted);
        Assert.False(transaction.IsRolledBack);
    }

    [Fact]
    public async Task GivenNewTransaction_WhenCommitAsyncCalled_ThenCallsSaveChangesThenCommit()
    {
        // Arrange
        var callOrder = new List<string>();

        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var mockDbContext = new Mock<DbContext>();

        mockDbContext
            .Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("SaveChanges"))
            .ReturnsAsync(0);

        mockContextTransaction
            .Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Commit"))
            .Returns(Task.CompletedTask);

        var transactionContext = new EfCoreTransactionContext(mockContextTransaction.Object);
        var transaction = new EfCoreUnitOfWorkTransaction(mockDbContext.Object, transactionContext);

        // Act
        await transaction.CommitAsync();

        // Assert
        Assert.Equal(["SaveChanges", "Commit"], callOrder);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenCommitAsyncCalledAgain_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var (transaction, _, _) = CreateTransaction();
        await transaction.CommitAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.CommitAsync());
        Assert.Contains("already been committed", exception.Message);
    }

    [Fact]
    public async Task GivenRolledBackTransaction_WhenCommitAsyncCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var (transaction, _, _) = CreateTransaction();
        await transaction.RollbackAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.CommitAsync());
        Assert.Contains("already been rolled back", exception.Message);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenRollbackAsyncCalled_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var (transaction, _, _) = CreateTransaction();
        await transaction.CommitAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.RollbackAsync());
        Assert.Contains("Cannot rollback a committed transaction", exception.Message);
    }

    [Fact]
    public async Task GivenNewTransaction_WhenRollbackAsyncCalled_ThenIsRolledBackTrue()
    {
        // Arrange
        var (transaction, _, _) = CreateTransaction();

        // Act
        await transaction.RollbackAsync();

        // Assert
        Assert.True(transaction.IsRolledBack);
        Assert.False(transaction.IsCommitted);
    }

    [Fact]
    public async Task GivenUncommittedTransaction_WhenDisposed_ThenAutoRollsBack()
    {
        // Arrange
        var (transaction, _, mockContextTransaction) = CreateTransaction();

        // Act
        await transaction.DisposeAsync();

        // Assert
        Assert.True(transaction.IsRolledBack);
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GivenCommittedTransaction_WhenDisposed_ThenDoesNotRollBack()
    {
        // Arrange
        var (transaction, _, mockContextTransaction) = CreateTransaction();
        await transaction.CommitAsync();

        // Act
        await transaction.DisposeAsync();

        // Assert
        Assert.True(transaction.IsCommitted);
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenRolledBackTransaction_WhenDisposed_ThenDoesNotRollBackAgain()
    {
        // Arrange
        var (transaction, _, mockContextTransaction) = CreateTransaction();
        await transaction.RollbackAsync();

        // Act
        await transaction.DisposeAsync();

        // Assert
        mockContextTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (EfCoreUnitOfWorkTransaction transaction, Mock<DbContext> mockDbContext, Mock<IDbContextTransaction> mockContextTransaction) CreateTransaction()
    {
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var mockDbContext = new Mock<DbContext>();

        mockDbContext
            .Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var transactionContext = new EfCoreTransactionContext(mockContextTransaction.Object);
        var transaction = new EfCoreUnitOfWorkTransaction(mockDbContext.Object, transactionContext);

        return (transaction, mockDbContext, mockContextTransaction);
    }
}
