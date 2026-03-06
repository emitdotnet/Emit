namespace Emit.EntityFrameworkCore.Tests;

using Emit;
using Emit.Abstractions;
using Emit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

public class EfCoreTransactionExtensionsTests
{
    [Fact]
    public async Task GivenNoActiveTransaction_WhenBeginTransactionAsync_ThenTransactionIsSet()
    {
        // Arrange
        var emitContext = new EmitContext();
        var mockDbContext = new Mock<DbContext>();
        var mockDatabase = new Mock<DatabaseFacade>(mockDbContext.Object);
        var mockContextTransaction = new Mock<IDbContextTransaction>();

        mockDbContext.Setup(c => c.Database).Returns(mockDatabase.Object);
        mockDatabase
            .Setup(d => d.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContextTransaction.Object);

        // Act
        var transaction = await emitContext.BeginTransactionAsync(mockDbContext.Object);

        // Assert
        Assert.NotNull(transaction);
        Assert.IsAssignableFrom<IEfCoreTransactionContext>(transaction);
        Assert.Same(transaction, emitContext.Transaction);
    }

    [Fact]
    public async Task GivenActiveTransaction_WhenBeginTransactionAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var emitContext = new EmitContext();
        var mockDbContext = new Mock<DbContext>();
        var existingTransaction = new Mock<ITransactionContext>();
        emitContext.Transaction = existingTransaction.Object;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            emitContext.BeginTransactionAsync(mockDbContext.Object));

        Assert.Contains("A transaction is already active", exception.Message);
    }

    [Fact]
    public async Task GivenNullContext_WhenBeginTransactionAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        IEmitContext emitContext = null!;
        var mockDbContext = new Mock<DbContext>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitContext.BeginTransactionAsync(mockDbContext.Object));
    }

    [Fact]
    public async Task GivenNullDbContext_WhenBeginTransactionAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var emitContext = new EmitContext();
        DbContext dbContext = null!;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitContext.BeginTransactionAsync(dbContext));
    }

    [Fact]
    public async Task GivenValidInputs_WhenBeginTransactionAsync_ThenReturnsEfCoreTransactionContext()
    {
        // Arrange
        var emitContext = new EmitContext();
        var mockDbContext = new Mock<DbContext>();
        var mockDatabase = new Mock<DatabaseFacade>(mockDbContext.Object);
        var mockContextTransaction = new Mock<IDbContextTransaction>();

        mockDbContext.Setup(c => c.Database).Returns(mockDatabase.Object);
        mockDatabase
            .Setup(d => d.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContextTransaction.Object);

        // Act
        var transaction = await emitContext.BeginTransactionAsync(mockDbContext.Object);

        // Assert
        Assert.IsType<EfCoreTransactionContext>(transaction);
    }
}
