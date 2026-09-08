namespace Emit.MongoDB.Tests;

using global::Emit;
using global::Emit.Abstractions;
using global::MongoDB.Driver;
using Moq;
using Xunit;

public class MongoUnitOfWorkTransactionTests
{
    private static (MongoUnitOfWorkTransaction transaction, Mock<IClientSessionHandle> mockSession, MongoTransactionContext transactionContext, MongoSessionHolder sessionHolder)
        CreateSut(IEmitContext? emitContext = null)
    {
        var mockSession = new Mock<IClientSessionHandle>();
        var transactionContext = new MongoTransactionContext { Session = mockSession.Object };
        var sessionHolder = new MongoSessionHolder { Session = mockSession.Object };
        var context = emitContext ?? new EmitContext();
        context.Transaction = transactionContext;
        var transaction = new MongoUnitOfWorkTransaction(mockSession.Object, transactionContext, sessionHolder, context);
        return (transaction, mockSession, transactionContext, sessionHolder);
    }

    [Fact]
    public async Task GivenMongoTransaction_WhenDisposed_ThenAmbientTransactionCleared()
    {
        // Arrange
        var emitContext = new EmitContext();
        var (transaction, _, _, _) = CreateSut(emitContext);
        Assert.NotNull(emitContext.Transaction);

        // Act
        await transaction.DisposeAsync();

        // Assert — a transaction left on the scoped context would collide with the next one.
        Assert.Null(emitContext.Transaction);
    }

    [Fact]
    public async Task GivenAmbientTransactionReplaced_WhenDisposed_ThenOtherTransactionNotCleared()
    {
        // Arrange — the context now holds a different transaction than the one being disposed.
        var emitContext = new EmitContext();
        var (transaction, _, _, _) = CreateSut(emitContext);
        emitContext.Transaction = null;
        var other = new MongoTransactionContext { Session = new Mock<IClientSessionHandle>().Object };
        emitContext.Transaction = other;

        // Act
        await transaction.DisposeAsync();

        // Assert — disposing one transaction must never detach an unrelated one.
        Assert.Same(other, emitContext.Transaction);
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
