namespace Emit.MongoDB.Tests;

using global::Emit.Abstractions;
using global::Emit.Models;
using global::Emit.MongoDB.Configuration;
using global::Emit.MongoDB.Models;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class MongoDbOutboxRepositoryTests
{
    #region Constructor Tests

    [Fact]
    public void GivenNullContext_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var mockEmitContext = new Mock<IEmitContext>();
        var logger = NullLogger<MongoDbOutboxRepository>.Instance;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MongoDbOutboxRepository(null!, mockEmitContext.Object, logger));
        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void GivenNullEmitContext_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var context = CreateMockContext();
        var logger = NullLogger<MongoDbOutboxRepository>.Instance;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MongoDbOutboxRepository(context, null!, logger));
        Assert.Equal("emitContext", exception.ParamName);
    }

    [Fact]
    public void GivenNullLogger_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var context = CreateMockContext();
        var mockEmitContext = new Mock<IEmitContext>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MongoDbOutboxRepository(context, mockEmitContext.Object, null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region EnqueueAsync Tests

    [Fact]
    public async Task GivenNullEntry_WhenEnqueueAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => repository.EnqueueAsync(null!));
        Assert.Equal("entry", exception.ParamName);
    }

    [Fact]
    public async Task GivenNoTransaction_WhenEnqueueAsync_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mockEmitContext = new Mock<IEmitContext>();
        mockEmitContext.Setup(x => x.Transaction).Returns((ITransactionContext?)null);

        var repository = CreateRepository(mockEmitContext.Object);
        var entry = new OutboxEntry { GroupKey = "test", SystemId = "kafka", Destination = "kafka://localhost:9092/test-topic" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.EnqueueAsync(entry));
        Assert.Contains("transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task GivenNullEntryId_WhenDeleteAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => repository.DeleteAsync(null!));
        Assert.Equal("entryId", exception.ParamName);
    }

    [Fact]
    public async Task GivenInvalidObjectIdString_WhenDeleteAsync_ThenThrowsFormatException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<FormatException>(
            () => repository.DeleteAsync("not-a-valid-objectid"));
    }

    [Fact]
    public async Task GivenUnsupportedEntryIdType_WhenDeleteAsync_ThenThrowsArgumentException()
    {
        // Arrange
        var repository = CreateRepository();
        var unsupportedId = 12345; // int is not supported

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => repository.DeleteAsync(unsupportedId));
        Assert.Equal("entryId", exception.ParamName);
        Assert.Contains("Int32", exception.Message);
    }

    #endregion

    #region GetBatchAsync Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GivenZeroOrNegativeBatchSize_WhenGetBatchAsync_ThenThrowsArgumentOutOfRangeException(int batchSize)
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.GetBatchAsync(batchSize));
        Assert.Equal("batchSize", exception.ParamName);
    }

    #endregion

    #region Helper Methods

    private static MongoDbOutboxRepository CreateRepository(IEmitContext? emitContext = null)
    {
        var context = CreateMockContext();
        emitContext ??= new Mock<IEmitContext>().Object;
        var logger = NullLogger<MongoDbOutboxRepository>.Instance;

        return new MongoDbOutboxRepository(context, emitContext, logger);
    }

    private static MongoDbContext CreateMockContext()
    {
        var mockOutboxCollection = new Mock<IMongoCollection<OutboxEntry>>();
        var mockCounterCollection = new Mock<IMongoCollection<SequenceCounter>>();
        var mockDatabase = new Mock<IMongoDatabase>();
        var mockClient = new Mock<IMongoClient>();

        return new MongoDbContext
        {
            Client = mockClient.Object,
            Database = mockDatabase.Object,
            OutboxCollection = mockOutboxCollection.Object,
            SequenceCollection = mockCounterCollection.Object
        };
    }

    #endregion
}
