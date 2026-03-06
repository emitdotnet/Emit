namespace Emit.MongoDB.Tests;

using Emit;
using Emit.Abstractions;
using Emit.MongoDB;
using global::MongoDB.Driver;
using Moq;
using Xunit;

public class MongoTransactionExtensionsTests
{
    [Fact]
    public async Task GivenNoActiveTransaction_WhenBeginMongoTransactionAsync_ThenTransactionIsSet()
    {
        // Arrange
        var emitContext = new EmitContext();
        var mockClient = new Mock<IMongoClient>();
        var mockSession = new Mock<IClientSessionHandle>();
        mockClient
            .Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSession.Object);

        // Act
        var transaction = await emitContext.BeginMongoTransactionAsync(mockClient.Object);

        // Assert
        Assert.NotNull(transaction);
        Assert.IsAssignableFrom<IMongoTransactionContext>(transaction);
        Assert.Same(transaction, emitContext.Transaction);
        mockSession.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
    }

    [Fact]
    public async Task GivenActiveTransaction_WhenBeginMongoTransactionAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var emitContext = new EmitContext();
        var mockClient = new Mock<IMongoClient>();
        var existingTransaction = new Mock<ITransactionContext>();
        emitContext.Transaction = existingTransaction.Object;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            emitContext.BeginMongoTransactionAsync(mockClient.Object));

        Assert.Contains("A transaction is already active", exception.Message);
    }

    [Fact]
    public async Task GivenNullContext_WhenBeginMongoTransactionAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        IEmitContext emitContext = null!;
        var mockClient = new Mock<IMongoClient>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitContext.BeginMongoTransactionAsync(mockClient.Object));
    }

    [Fact]
    public async Task GivenNullMongoClient_WhenBeginMongoTransactionAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var emitContext = new EmitContext();
        IMongoClient mongoClient = null!;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            emitContext.BeginMongoTransactionAsync(mongoClient));
    }

    [Fact]
    public async Task GivenValidInputs_WhenBeginMongoTransactionAsync_ThenReturnsMongoTransactionContext()
    {
        // Arrange
        var emitContext = new EmitContext();
        var mockClient = new Mock<IMongoClient>();
        var mockSession = new Mock<IClientSessionHandle>();
        mockClient
            .Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSession.Object);

        // Act
        var transaction = await emitContext.BeginMongoTransactionAsync(mockClient.Object);

        // Assert
        Assert.IsType<MongoTransactionContext>(transaction);
        Assert.Same(mockSession.Object, transaction.Session);
    }
}
