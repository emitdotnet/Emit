namespace Emit.MongoDB.Tests;

using global::MongoDB.Driver;
using Moq;
using Xunit;

public class MongoUnitOfWorkTransactionTests
{
    private static (MongoUnitOfWorkTransaction transaction, Mock<IClientSessionHandle> mockSession, MongoTransactionContext transactionContext, MongoSessionHolder sessionHolder)
        CreateSut()
    {
        var mockSession = new Mock<IClientSessionHandle>();
        var transactionContext = new MongoTransactionContext { Session = mockSession.Object };
        var sessionHolder = new MongoSessionHolder { Session = mockSession.Object };
        var transaction = new MongoUnitOfWorkTransaction(mockSession.Object, transactionContext, sessionHolder);
        return (transaction, mockSession, transactionContext, sessionHolder);
    }

    [Fact]
    public async Task GivenMongoTransaction_WhenCommitAsyncCalled_ThenDelegatesToTransactionContext()
    {
        // Arrange
        var (transaction, mockSession, _, _) = CreateSut();

        // Act
        await transaction.CommitAsync();

        // Assert
        mockSession.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(transaction.IsCommitted);
    }

    [Fact]
    public async Task GivenMongoTransaction_WhenRollbackAsyncCalled_ThenDelegatesToTransactionContext()
    {
        // Arrange
        var (transaction, mockSession, _, _) = CreateSut();

        // Act
        await transaction.RollbackAsync();

        // Assert
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(transaction.IsRolledBack);
    }

    [Fact]
    public void GivenMongoTransaction_WhenSessionAccessed_ThenReturnsClientSessionHandle()
    {
        // Arrange
        var (transaction, mockSession, _, _) = CreateSut();

        // Act
        var session = transaction.Session;

        // Assert
        Assert.Same(mockSession.Object, session);
    }

    [Fact]
    public async Task GivenUncommittedMongoTransaction_WhenDisposed_ThenAutoRollsBack()
    {
        // Arrange
        var (transaction, mockSession, _, sessionHolder) = CreateSut();

        // Act
        await transaction.DisposeAsync();

        // Assert
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(sessionHolder.Session);
    }

    [Fact]
    public async Task GivenCommittedMongoTransaction_WhenDisposed_ThenDoesNotRollBack()
    {
        // Arrange
        var (transaction, mockSession, _, sessionHolder) = CreateSut();
        await transaction.CommitAsync();

        // Act
        await transaction.DisposeAsync();

        // Assert
        mockSession.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(sessionHolder.Session);
    }
}
