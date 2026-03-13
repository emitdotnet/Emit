namespace Emit.MongoDB.Tests;

using Emit;
using Emit.Abstractions;
using Emit.MongoDB;
using Emit.MongoDB.Configuration;
using global::MongoDB.Driver;
using Moq;
using Xunit;

public class MongoUnitOfWorkTests
{
    private static (MongoUnitOfWork unitOfWork, Mock<IClientSessionHandle> mockSession, EmitContext emitContext, MongoSessionHolder sessionHolder)
        CreateSut()
    {
        var mockSession = new Mock<IClientSessionHandle>();
        var mockClient = new Mock<IMongoClient>();
        mockClient
            .Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSession.Object);

        var mongoContext = new MongoDbContext { Client = mockClient.Object, Database = null! };
        var emitContext = new EmitContext();
        var sessionHolder = new MongoSessionHolder();
        var unitOfWork = new MongoUnitOfWork(mongoContext, emitContext, sessionHolder);

        return (unitOfWork, mockSession, emitContext, sessionHolder);
    }

    [Fact]
    public async Task GivenMongoUnitOfWork_WhenBeginAsyncCalled_ThenStartsSessionAndTransaction()
    {
        // Arrange
        var (unitOfWork, mockSession, _, _) = CreateSut();

        // Act
        await using var transaction = await unitOfWork.BeginAsync();

        // Assert
        mockSession.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
    }

    [Fact]
    public async Task GivenMongoUnitOfWork_WhenBeginAsyncCalled_ThenSetsEmitContextTransaction()
    {
        // Arrange
        var (unitOfWork, _, emitContext, _) = CreateSut();

        // Act
        await using var transaction = await unitOfWork.BeginAsync();

        // Assert
        Assert.NotNull(emitContext.Transaction);
        Assert.IsAssignableFrom<ITransactionContext>(emitContext.Transaction);
    }

    [Fact]
    public async Task GivenMongoUnitOfWork_WhenBeginAsyncCalled_ThenSetsSessionOnHolder()
    {
        // Arrange
        var (unitOfWork, mockSession, _, sessionHolder) = CreateSut();

        // Act
        await using var transaction = await unitOfWork.BeginAsync();

        // Assert
        Assert.Same(mockSession.Object, sessionHolder.Session);
    }

    [Fact]
    public async Task GivenMongoUnitOfWork_WhenBeginAsyncCalled_ThenReturnedTransactionImplementsIMongoUnitOfWorkTransaction()
    {
        // Arrange
        var (unitOfWork, _, _, _) = CreateSut();

        // Act
        await using var transaction = await unitOfWork.BeginAsync();

        // Assert
        Assert.IsAssignableFrom<IMongoUnitOfWorkTransaction>(transaction);
    }
}
