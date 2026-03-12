namespace Emit.EntityFrameworkCore.Tests;

using global::Emit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class EfCoreOutboxRepositoryTests
{
    #region Constructor Tests

    [Fact]
    public void GivenNullDbContext_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var dbContextFactory = CreateMockDbContextFactory();
        var logger = NullLogger<EfCoreOutboxRepository<TestDbContext>>.Instance;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new EfCoreOutboxRepository<TestDbContext>(null!, dbContextFactory, logger));
        Assert.Equal("dbContext", exception.ParamName);
    }

    [Fact]
    public void GivenNullDbContextFactory_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var mockDbContext = new Mock<TestDbContext>();
        var logger = NullLogger<EfCoreOutboxRepository<TestDbContext>>.Instance;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new EfCoreOutboxRepository<TestDbContext>(mockDbContext.Object, null!, logger));
        Assert.Equal("dbContextFactory", exception.ParamName);
    }

    [Fact]
    public void GivenNullLogger_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var mockDbContext = new Mock<TestDbContext>();
        var dbContextFactory = CreateMockDbContextFactory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new EfCoreOutboxRepository<TestDbContext>(mockDbContext.Object, dbContextFactory, null!));
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
    public async Task GivenInvalidGuidString_WhenDeleteAsync_ThenThrowsFormatException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<FormatException>(
            () => repository.DeleteAsync("not-a-valid-guid"));
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

    private static EfCoreOutboxRepository<TestDbContext> CreateRepository()
    {
        var mockDbContext = new Mock<TestDbContext>();
        var mockDbContextFactory = CreateMockDbContextFactory();
        var logger = NullLogger<EfCoreOutboxRepository<TestDbContext>>.Instance;

        return new EfCoreOutboxRepository<TestDbContext>(
            mockDbContext.Object,
            mockDbContextFactory,
            logger);
    }

    private static IDbContextFactory<TestDbContext> CreateMockDbContextFactory()
    {
        var mockFactory = new Mock<IDbContextFactory<TestDbContext>>();
        var mockDbContext = new Mock<TestDbContext>();

        mockFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockDbContext.Object);

        return mockFactory.Object;
    }

    // Test DbContext class for mocking
    public class TestDbContext : DbContext
    {
        public TestDbContext() : base(new DbContextOptions<TestDbContext>())
        {
        }
    }

    #endregion
}
