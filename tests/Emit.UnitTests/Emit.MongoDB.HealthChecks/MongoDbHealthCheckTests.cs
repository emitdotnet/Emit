namespace Emit.MongoDB.HealthChecks.Tests;

using Emit.MongoDB.Configuration;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

public class MongoDbHealthCheckTests
{
    private static MongoDbContext CreateContext(IMongoDatabase database) =>
        new() { Client = Mock.Of<IMongoClient>(), Database = database };

    private static HealthCheckContext CreateHealthCheckContext(HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "test",
                _ => Mock.Of<IHealthCheck>(),
                failureStatus,
                [])
        };

    [Fact]
    public async Task GivenPingSucceeds_WhenCheckHealth_ThenReturnsHealthy()
    {
        // Arrange
        var databaseMock = new Mock<IMongoDatabase>();
        databaseMock
            .Setup(d => d.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BsonDocument("ok", 1));

        var check = new MongoDbHealthCheck(CreateContext(databaseMock.Object));

        // Act
        var result = await check.CheckHealthAsync(CreateHealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task GivenPingThrows_WhenCheckHealth_ThenReturnsUnhealthy()
    {
        // Arrange
        var databaseMock = new Mock<IMongoDatabase>();
        databaseMock
            .Setup(d => d.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Server selection timed out"));

        var check = new MongoDbHealthCheck(CreateContext(databaseMock.Object));

        // Act
        var result = await check.CheckHealthAsync(CreateHealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<TimeoutException>(result.Exception);
    }

    [Fact]
    public async Task GivenCustomFailureStatus_WhenCheckHealth_ThenReturnsConfiguredStatus()
    {
        // Arrange
        var databaseMock = new Mock<IMongoDatabase>();
        databaseMock
            .Setup(d => d.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Server selection timed out"));

        var check = new MongoDbHealthCheck(CreateContext(databaseMock.Object));

        // Act
        var result = await check.CheckHealthAsync(CreateHealthCheckContext(failureStatus: HealthStatus.Degraded));

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }
}
