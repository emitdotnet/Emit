namespace Emit.EntityFrameworkCore.Tests;

using Emit;
using Emit.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

public class EfCoreUnitOfWorkTests
{
    [Fact]
    public async Task GivenEfCoreUnitOfWork_WhenBeginAsyncCalled_ThenReturnsTransaction()
    {
        // Arrange
        var (unitOfWork, _, _) = CreateUnitOfWork();

        // Act
        var transaction = await unitOfWork.BeginAsync();

        // Assert
        Assert.NotNull(transaction);
        Assert.IsType<EfCoreUnitOfWorkTransaction>(transaction);
    }

    [Fact]
    public async Task GivenEfCoreUnitOfWork_WhenBeginAsyncCalled_ThenSetsEmitContextTransaction()
    {
        // Arrange
        var (unitOfWork, emitContext, _) = CreateUnitOfWork();

        // Act
        await unitOfWork.BeginAsync();

        // Assert
        Assert.NotNull(emitContext.Transaction);
        Assert.IsType<EfCoreTransactionContext>(emitContext.Transaction);
    }

    private static (EfCoreUnitOfWork<DbContext> unitOfWork, IEmitContext emitContext, Mock<IDbContextTransaction> mockTransaction) CreateUnitOfWork()
    {
        var mockContextTransaction = new Mock<IDbContextTransaction>();
        var mockDbContext = new Mock<DbContext>();
        var mockDatabase = new Mock<DatabaseFacade>(mockDbContext.Object);

        mockDbContext.Setup(c => c.Database).Returns(mockDatabase.Object);
        mockDatabase
            .Setup(d => d.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContextTransaction.Object);

        var emitContext = new EmitContext();
        var unitOfWork = new EfCoreUnitOfWork<DbContext>(mockDbContext.Object, emitContext);

        return (unitOfWork, emitContext, mockContextTransaction);
    }
}
